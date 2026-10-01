using WeatherTelegramBot.Models;

namespace WeatherTelegramBot.Services.Interfaces
{
    public interface IPrediccionService
    {
        /// <summary>
        /// Descarga el feed de el-tiempo.net y lo normaliza a <see cref="PrediccionMunicipio"/>.
        /// Devuelve null si la API falla o responde algo que no se puede interpretar.
        /// </summary>
        Task<PrediccionMunicipio?> ObtenerPrediccionAsync(
            string codProvincia,
            string codMunicipio,
            CancellationToken cancellationToken = default);
    }
}