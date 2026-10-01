namespace WeatherTelegramBot.Models
{
    /// <summary>
    /// Gasolinera ya normalizada: importe en double y distancia calculada. Solo se
    /// conservan los datos que el bot muestra, así que el resto del feed del MITECO se
    /// descarta al deserializar.
    /// </summary>
    public record Gasolinera(
        string Nombre,
        string Direccion,
        double Precio,
        double DistanciaKm
    );
}
