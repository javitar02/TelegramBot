using WeatherTelegramBot.Models;

namespace WeatherTelegramBot.Services.Interfaces
{
    public interface IGasolinaService
    {
        /// <summary>
        /// Gasolineras del término municipal indicado con precio publicado del carburante
        /// pedido, ordenadas de más barata a más cara. Las estaciones que no publican ese
        /// precio quedan fuera.
        /// </summary>
        /// <param name="idMunicipio">
        /// IDMunicipio del feed del MITECO (6058 es Alcalá de Guadaíra). Se filtra por
        /// pertenencia al municipio y no por distancia: con un radio se cuela el vecino que
        /// está justo al otro lado del linde, que no es lo que se pidió.
        /// </param>
        Task<IReadOnlyList<Gasolinera>> ObtenerGasolinerasCercaAsync(
            string codProvincia,
            string idMunicipio,
            double latitud,
            double longitud,
            TipoCarburante carburante,
            CancellationToken cancellationToken = default);
    }
}
