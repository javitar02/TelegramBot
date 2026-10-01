using System.Text.Json.Serialization;

namespace WeatherTelegramBot.Responses
{
    public record PreciosCarburantesResponse(
        [property: JsonPropertyName("Fecha")] string? Fecha,
        [property: JsonPropertyName("ListaEESSPrecio")] List<EstacionServicioDto>? ListaEESSPrecio,
        [property: JsonPropertyName("Nota")] string? Nota
    );

    /// <summary>
    /// Tal y como lo publica el MITECO, los importes y las coordenadas llegan como texto
    /// con coma decimal ("1,849" / "37,474333"). Se convierten a double en GasolinaService.
    /// </summary>
    public record EstacionServicioDto(
        [property: JsonPropertyName("Rótulo")] string? Rotulo,
        [property: JsonPropertyName("Dirección")] string? Direccion,
        [property: JsonPropertyName("Municipio")] string? Municipio,
        [property: JsonPropertyName("Provincia")] string? Provincia,
        [property: JsonPropertyName("Latitud")] string? Latitud,
        [property: JsonPropertyName("Longitud (WGS84)")] string? Longitud,
        [property: JsonPropertyName("Precio Gasolina 95 E5")] string? PrecioGasolina95,
        [property: JsonPropertyName("Precio Gasolina 98 E5")] string? PrecioGasolina98,
        [property: JsonPropertyName("Precio Gasoleo A")] string? PrecioGasoleoA,
        [property: JsonPropertyName("Horario")] string? Horario,
        [property: JsonPropertyName("IDEESS")] string? Ideess
    );
}
