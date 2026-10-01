namespace WeatherTelegramBot.Services
{
    /// <summary>
    /// Datos comunes del servicio externo el-tiempo.net, usado para el clima y la previsión.
    /// </summary>
    internal static class ElTiempoApi
    {
        public const string BaseUrl = "https://api.el-tiempo.net/json/v3";
        public const string NombreCliente = "eltiempo";
    }
}
