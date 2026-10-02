namespace WeatherTelegramBot.Models
{
    /// <summary>
    /// Gasolinera ya normalizada: importe en double y distancia calculada. Solo se
    /// conservan los datos que el bot muestra, así que el resto del feed del MITECO se
    /// descarta al deserializar. Las coordenadas se guardan para poder enlazar la dirección
    /// con Google Maps: la dirección a secas puede ser ambigua y el mapa acabaría en otra calle.
    /// </summary>
    public record Gasolinera(
        string Nombre,
        string Direccion,
        double Precio,
        double DistanciaKm,
        double Latitud,
        double Longitud
    );
}
