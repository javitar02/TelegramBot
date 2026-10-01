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

        /// <summary>
        /// Identificador de municipio del feed de carburantes del MITECO. No es el INE (41004)
        /// sino el código interno que usa el Ministerio, que es lo que viene en cada estación
        /// como IDMunicipio. Sirve para quedarse solo con las gasolineras del término municipal.
        /// </summary>
        public const string IdMunicipio = "6058";
    }
}
