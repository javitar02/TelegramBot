namespace WeatherTelegramBot.Services
{
    /// <summary>
    /// El bot solo da servicio en Alcalá de Guadaíra, así que sus datos son fijos y no hace
    /// falta descargar el catálogo de municipios de la provincia. Las coordenadas son el
    /// centroide del término municipal.
    /// </summary>
    internal static class MunicipioAlcala
    {
        /// <summary>Código INE abreviado, el que usan las URLs de el-tiempo.net.</summary>
        public const string CodigoIne = "41004";

        public const string Nombre = "Alcalá de Guadaíra";

        public const string CodigoProvincia = "41";

        public const double Latitud = 37.463;

        public const double Longitud = -5.981;
    }
}
