using WeatherTelegramBot.Responses;

namespace WeatherTelegramBot.Services
{
    public interface IWeatherService
    {
        Task<TiempoResponse?> ObtenerTiempoPorMunicipioAsync(
            string codProvincia,
            string codMunicipio,
            CancellationToken cancellationToken = default);
    }
}
