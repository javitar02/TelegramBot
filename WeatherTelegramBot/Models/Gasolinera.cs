namespace WeatherTelegramBot.Models
{
    /// <summary>
    /// Gasolinera ya normalizada: importes en double y distancia calculada. Los tres
    /// precios que publica el MITECO vienen como opcionales porque cada estación puede
    /// tener alguno sin publicar; <see cref="Precio"/> es el del carburante consultado y
    /// nunca nulo, ya que el servicio descarta las estaciones que no lo tienen.
    /// </summary>
    public record Gasolinera(
        string Nombre,
        string Direccion,
        string Municipio,
        double Latitud,
        double Longitud,
        double DistanciaKm,
        TipoCarburante Carburante,
        double Precio,
        double? PrecioGasolina95,
        double? PrecioGasolina98,
        double? PrecioGasoleoA,
        string? Horario
    );

    public record ResultadoGasolineras(
        IReadOnlyList<Gasolinera> Gasolineras,
        TipoCarburante Carburante,
        string? Actualizado
    );
}