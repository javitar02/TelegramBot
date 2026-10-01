using WeatherTelegramBot.Responses;

namespace WeatherTelegramBot.Services.Interfaces
{
    public interface IWeatherService
    {
        Task<TiempoResponse?> ObtenerTiempoPorMunicipioAsync(
            string codProvincia,
            string codMunicipio,
            CancellationToken cancellationToken = default);
    }
}
