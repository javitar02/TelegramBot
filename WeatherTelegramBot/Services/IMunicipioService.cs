using WeatherTelegramBot.Responses;

namespace WeatherTelegramBot.Services
{
    public interface IMunicipioService
    {
        Task<IReadOnlyList<MunicipioCatalogo>> ObtenerMunicipiosAsync(CancellationToken cancellationToken = default);
    }
}