using System.Text.Json.Serialization;

namespace WeatherTelegramBot.Responses
{
    public record PreciosCarburantesResponse(
        [property: JsonPropertyName("Fecha")] string? Fecha,
        [property: JsonPropertyName("ListaEESSPrecio")] List<EstacionServicioDto>? ListaEESSPrecio
    );

    /// <summary>
    /// Tal y como lo publica el MITECO, los importes y las coordenadas llegan como texto
    /// con coma decimal ("1,849" / "37,474333"). Se convierten a double en GasolinaService.
    /// </summary>
    public record EstacionServicioDto(
        [property: JsonPropertyName("Rótulo")] string? Rotulo,
        [property: JsonPropertyName("Dirección")] string? Direccion,
        [property: JsonPropertyName("Latitud")] string? Latitud,
        [property: JsonPropertyName("Longitud (WGS84)")] string? Longitud,
        [property: JsonPropertyName("Precio Gasolina 95 E5")] string? PrecioGasolina95,
        [property: JsonPropertyName("Precio Gasoleo A")] string? PrecioGasoleoA
    );
}
