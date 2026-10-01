using System.Net.Http.Json;
using System.Text.Json;
using WeatherTelegramBot.Responses;

namespace WeatherTelegramBot.Services
{
    public class WeatherService : IWeatherService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WeatherService> _logger;

        public WeatherService(IHttpClientFactory httpClientFactory, ILogger<WeatherService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<TiempoResponse?> ObtenerTiempoPorMunicipioAsync(
            string codProvincia,
            string codMunicipio,
            CancellationToken cancellationToken = default)
        {
            string url = $"{ElTiempoApi.BaseUrl}/provincias/{codProvincia}/municipios/{codMunicipio}";

            try
            {
                var httpClient = _httpClientFactory.CreateClient(ElTiempoApi.NombreCliente);
                return await httpClient.GetFromJsonAsync<TiempoResponse>(url, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error de red al consultar el clima de {Provincia}/{Municipio}", codProvincia, codMunicipio);
                return null;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error al deserializar el clima de {Provincia}/{Municipio}", codProvincia, codMunicipio);
                return null;
            }
        }
    }
}
