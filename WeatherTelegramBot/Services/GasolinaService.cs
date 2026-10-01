using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using WeatherTelegramBot.Models;
using WeatherTelegramBot.Responses;

namespace WeatherTelegramBot.Services
{
    /// <summary>
    /// Consulta los precios de carburantes al API REST del MITECO (Geoportal de
    /// Hidrocarburos). Fuente oficial: https://datos.gob.es (CC BY 4.0), por lo que hay
    /// que citar al Ministerio y mostrar la fecha de los datos.
    /// </summary>
    public class GasolinaService : IGasolinaService
    {
        private const string BaseUrl = "https://sedeaplicaciones.minetur.gob.es/ServiciosRESTCarburantes/PreciosCarburantes";
        private const string NombreClienteMiteco = "miteco";

        /// <summary>El Ministerio refresca los precios cada media hora, así que 30 min es un TTL razonable.</summary>
        private static readonly TimeSpan CacheValidez = TimeSpan.FromMinutes(30);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<GasolinaService> _logger;
        private readonly SemaphoreSlim _cargaLock = new(1, 1);

        private CacheEstaciones? _cache;

        public GasolinaService(IHttpClientFactory httpClientFactory, ILogger<GasolinaService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<ResultadoGasolineras> ObtenerGasolinerasCercaAsync(
            string codProvincia,
            double latitud,
            double longitud,
            double radioKm,
            TipoCarburante carburante,
            CancellationToken cancellationToken = default)
        {
            var (estaciones, actualizado) = await ObtenerEstacionesAsync(codProvincia, cancellationToken);

            var candidatas = new List<Gasolinera>();
            var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var estacion in estaciones)
            {
                if (!TryParseDecimal(estacion.Latitud, out double estLat) ||
                    !TryParseDecimal(estacion.Longitud, out double estLon))
                    continue;

                double distancia = DistanciaKm(latitud, longitud, estLat, estLon);
                if (distancia > radioKm)
                    continue;

                if (!TryParseDecimal(PrecioPublicado(estacion, carburante), out double precio))
                    continue;

                // El MITECO devuelve filas repetidas cuando la misma estación está dada de
                // alta dos veces (mismo rótulo a unos pocos metros). Se deduplica por
                // marca + posición redondeada a 3 decimales (~110 m).
                string clave = $"{estacion.Rotulo}|{estLat:F3}|{estLon:F3}";
                if (!vistas.Add(clave))
                    continue;

                candidatas.Add(new Gasolinera(
                    Nombre: (estacion.Rotulo ?? "Sin rótulo").Trim(),
                    Direccion: (estacion.Direccion ?? "").Trim(),
                    Municipio: (estacion.Municipio ?? "").Trim(),
                    Latitud: estLat,
                    Longitud: estLon,
                    DistanciaKm: distancia,
                    Carburante: carburante,
                    Precio: precio,
                    PrecioGasolina95: TryParseDecimal(estacion.PrecioGasolina95, out double g95) ? g95 : null,
                    PrecioGasolina98: TryParseDecimal(estacion.PrecioGasolina98, out double g98) ? g98 : null,
                    PrecioGasoleoA: TryParseDecimal(estacion.PrecioGasoleoA, out double goa) ? goa : null,
                    Horario: estacion.Horario));
            }

            var ordenadas = candidatas
                .OrderBy(g => g.Precio)
                .ThenBy(g => g.DistanciaKm)
                .ToList();

            _logger.LogInformation(
                "Gasolineras de {Carburante} en {Provincia}: {Total} en el feed, {Cercanas} a menos de {Radio} km de ({Lat},{Lon})",
                carburante.Nombre(), codProvincia, estaciones.Count, ordenadas.Count, radioKm, latitud, longitud);

            return new ResultadoGasolineras(ordenadas, carburante, actualizado);
        }

        /// <summary>Columna del feed que corresponde al carburante pedido.</summary>
        private static string? PrecioPublicado(EstacionServicioDto estacion, TipoCarburante carburante) =>
            carburante switch
            {
                TipoCarburante.GasoleoA => estacion.PrecioGasoleoA,
                _ => estacion.PrecioGasolina95
            };

        private async Task<(IReadOnlyList<EstacionServicioDto> Estaciones, string? Actualizado)> ObtenerEstacionesAsync(
            string codProvincia,
            CancellationToken cancellationToken)
        {
            if (EsCacheValida(codProvincia))
                return (_cache!.Estaciones, _cache.Actualizado);

            await _cargaLock.WaitAsync(cancellationToken);
            try
            {
                if (EsCacheValida(codProvincia))
                    return (_cache!.Estaciones, _cache.Actualizado);

                string url = $"{BaseUrl}/EstacionesTerrestres/FiltroProvincia/{codProvincia}";
                var httpClient = _httpClientFactory.CreateClient(NombreClienteMiteco);

                using var response = await httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("El API de carburantes devolvió {StatusCode} al consultar {Url}", (int)response.StatusCode, url);
                    return (Array.Empty<EstacionServicioDto>(), null);
                }

                var datos = await response.Content
                    .ReadFromJsonAsync<PreciosCarburantesResponse>(cancellationToken)
                    .ConfigureAwait(false);

                if (datos?.ListaEESSPrecio is null || datos.ListaEESSPrecio.Count == 0)
                {
                    _logger.LogError("El API de carburantes devolvió un listado vacío para la provincia {Provincia}", codProvincia);
                    return (Array.Empty<EstacionServicioDto>(), datos?.Fecha);
                }

                _cache = new CacheEstaciones(codProvincia, datos.Fecha, DateTimeOffset.UtcNow, datos.ListaEESSPrecio);

                _logger.LogInformation(
                    "Precios de carburantes cargados: {Cantidad} estaciones en la provincia {Provincia} (MITECO {Fecha})",
                    datos.ListaEESSPrecio.Count, codProvincia, datos.Fecha);

                return (_cache.Estaciones, _cache.Actualizado);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error de red al consultar los precios de carburantes");
                return (Array.Empty<EstacionServicioDto>(), null);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error al deserializar la respuesta de la API de carburantes");
                return (Array.Empty<EstacionServicioDto>(), null);
            }
            finally
            {
                _cargaLock.Release();
            }
        }

        private bool EsCacheValida(string codProvincia)
        {
            return _cache is not null &&
                   _cache.Provincia == codProvincia &&
                   DateTimeOffset.UtcNow - _cache.Cargada < CacheValidez;
        }

        /// <summary>
        /// El MITECO usa coma decimal, así que se normaliza a punto antes de parsear.
        /// Con NumberStyles.Float y la cultura invariante "1,849" no se interpretaría bien.
        /// </summary>
        internal static bool TryParseDecimal(string? valor, out double resultado)
        {
            resultado = 0;

            if (string.IsNullOrWhiteSpace(valor))
                return false;

            return double.TryParse(
                valor.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out resultado);
        }

        internal static double DistanciaKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double RadioTierraKm = 6371.0;
            const double GradosARadianes = Math.PI / 180.0;

            double dLat = (lat2 - lat1) * GradosARadianes;
            double dLon = (lon2 - lon1) * GradosARadianes;

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * GradosARadianes) * Math.Cos(lat2 * GradosARadianes) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            return RadioTierraKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private sealed record CacheEstaciones(
            string Provincia,
            string? Actualizado,
            DateTimeOffset Cargada,
            IReadOnlyList<EstacionServicioDto> Estaciones);
    }
}
