namespace WeatherTelegramBot.Models
{
    /// <summary>
    /// Carburantes que el usuario puede comparar. El bot no guarda ningún estado: el tipo
    /// viaja dentro del callbackData, así que necesita un token corto (Telegram limita
    /// CallbackData a 64 bytes).
    /// </summary>
    public enum TipoCarburante
    {
        Gasolina95,
        GasoleoA
    }

    public static class TipoCarburanteExtensions
    {
        /// <summary>Token de dos o tres caracteres con el que viaja por el callbackData.</summary>
        public static string Token(this TipoCarburante tipo) => tipo switch
        {
            TipoCarburante.GasoleoA => "di",
            _ => "95"
        };

        public static TipoCarburante? DesdeToken(string? token) => token switch
        {
            "95" => TipoCarburante.Gasolina95,
            "di" => TipoCarburante.GasoleoA,
            _ => null
        };

        /// <summary>Nombre tal y como lo etiqueta el MITECO en el feed.</summary>
        public static string Nombre(this TipoCarburante tipo) => tipo switch
        {
            TipoCarburante.GasoleoA => "Gasóleo A",
            _ => "Gasolina 95 E5"
        };

        /// <summary>
        /// Nombre corto para la cabecera del listado, que solo lleva el tipo: allí la
        /// graduación estorba ("Diesel", "Gasolina").
        /// </summary>
        public static string Titulo(this TipoCarburante tipo) => tipo switch
        {
            TipoCarburante.GasoleoA => "Diesel",
            _ => "Gasolina"
        };

        /// <summary>
        /// El carburante contrario: de diesel pasa a gasolina y viceversa. Así el botón
        /// "Cambiar carburante" puede tener comportamiento de toggle automático.
        /// </summary>
        public static TipoCarburante Contrario(this TipoCarburante tipo) => tipo switch
        {
            TipoCarburante.GasoleoA => TipoCarburante.Gasolina95,
            _ => TipoCarburante.GasoleoA
        };

        public static string Boton(this TipoCarburante tipo) => tipo switch
        {
            TipoCarburante.GasoleoA => "🛢️ Diesel (Gasóleo A)",
            _ => "⛽ Gasolina 95 E5"
        };
    }
}