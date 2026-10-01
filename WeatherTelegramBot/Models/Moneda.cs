namespace WeatherTelegramBot.Models
{
    /// <summary>
    /// Moneda en la que se enseñan los precios. Igual que el carburante, no se guarda en
    /// ningún sitio: viaja dentro del callbackData, así que necesita un token corto.
    /// Euros es el valor por defecto del enum y el punto de partida de cualquier listado;
    /// las pesetas solo aparecen si el usuario pulsa el botón.
    /// </summary>
    internal enum Moneda
    {
        Euros,
        Pesetas
    }

    internal static class MonedaExtensions
    {
        /// <summary>Token de dos caracteres con el que viaja por el callbackData.</summary>
        public static string Token(this Moneda moneda) => moneda switch
        {
            Moneda.Pesetas => "pt",
            _ => "eu"
        };

        /// <summary>Moneda a la que salta el botón de alternar.</summary>
        public static Moneda Contraria(this Moneda moneda) => moneda switch
        {
            Moneda.Pesetas => Moneda.Euros,
            _ => Moneda.Pesetas
        };

        public static Moneda? DesdeToken(string? token) => token switch
        {
            "eu" => Moneda.Euros,
            "pt" => Moneda.Pesetas,
            _ => null
        };

        /// <summary>
        /// El rótulo del botón dice a lo que se cambia, no a lo que se está: si se ve en
        /// euros, lo que ofrece el botón son las pesetas.
        /// </summary>
        public static string Boton(this Moneda moneda) => moneda switch
        {
            Moneda.Pesetas => "💶 Ver en euros",
            _ => "💱 Ver en pesetas"
        };
    }
}
