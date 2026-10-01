namespace WeatherTelegramBot.Services
{
    /// <summary>
    /// Datos comunes del servicio externo el-tiempo.net, usado tanto para el catálogo
    /// de municipios como para la previsión meteorológica.
    /// </summary>
    internal static class ElTiempoApi
    {
        public const string BaseUrl = "https://api.el-tiempo.net/json/v3";
        public const string NombreCliente = "eltiempo";
        public const string CodProvinciaSevilla = "41";
    }
}
