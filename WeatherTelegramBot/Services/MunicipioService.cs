using System.Net.Http.Json;
using System.Text.Json;
using WeatherTelegramBot.Responses;

namespace WeatherTelegramBot.Services
{
    public class MunicipioService : IMunicipioService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MunicipioService> _logger;
        private readonly SemaphoreSlim _cargaLock = new(1, 1);

        private IReadOnlyList<MunicipioCatalogo>? _cache;

        public MunicipioService(IHttpClientFactory httpClientFactory, ILogger<MunicipioService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<IReadOnlyList<MunicipioCatalogo>> ObtenerMunicipiosAsync(CancellationToken cancellationToken = default)
        {
            if (_cache is not null)
                return _cache;

            await _cargaLock.WaitAsync(cancellationToken);
            try
            {
                if (_cache is not null)
                    return _cache;

                string url = $"{ElTiempoApi.BaseUrl}/provincias/{ElTiempoApi.CodProvinciaSevilla}/municipios";
                var httpClient = _httpClientFactory.CreateClient(ElTiempoApi.NombreCliente);
                var datos = await httpClient.GetFromJsonAsync<MunicipiosProvinciaResponse>(url, cancellationToken);

                if (datos?.Municipios is null || datos.Municipios.Count == 0)
                {
                    _logger.LogError("El catálogo de municipios de Sevilla llegó vacío desde {Url}", url);
                    return Array.Empty<MunicipioCatalogo>();
                }

                _cache = datos.Municipios
                    .OrderBy(m => m.Nombre, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                _logger.LogInformation("Catálogo cargado: {Cantidad} municipios de {Provincia}", _cache.Count, datos.Provincia);
                return _cache;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error de red al cargar el catálogo de municipios");
                return Array.Empty<MunicipioCatalogo>();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error al deserializar el catálogo de municipios");
                return Array.Empty<MunicipioCatalogo>();
            }
            finally
            {
                _cargaLock.Release();
            }
        }
    }
}
