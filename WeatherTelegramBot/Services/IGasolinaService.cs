using WeatherTelegramBot.Models;

namespace WeatherTelegramBot.Services
{
    public interface IGasolinaService
    {
        /// <summary>
        /// Gasolineras con precio publicado del carburante indicado dentro del radio, ordenadas
        /// de más barata a más cara. Las estaciones que no publican ese precio quedan fuera.
        /// </summary>
        Task<ResultadoGasolineras> ObtenerGasolinerasCercaAsync(
            string codProvincia,
            double latitud,
            double longitud,
            double radioKm,
            TipoCarburante carburante,
            CancellationToken cancellationToken = default);
    }
}