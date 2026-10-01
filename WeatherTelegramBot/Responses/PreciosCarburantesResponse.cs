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
    /// IDMunicipio es el identificador que usa el MITECO para el municipio (6058 es Alcalá
    /// de Guadaíra) y no el INE: es lo que permite quedarse solo con las estaciones del
    /// término municipal sin fiarse del nombre, que viene con tildes y acentos.
    /// </summary>
    public record EstacionServicioDto(
        [property: JsonPropertyName("Rótulo")] string? Rotulo,
        [property: JsonPropertyName("Dirección")] string? Direccion,
        [property: JsonPropertyName("Latitud")] string? Latitud,
        [property: JsonPropertyName("Longitud (WGS84)")] string? Longitud,
        [property: JsonPropertyName("Precio Gasolina 95 E5")] string? PrecioGasolina95,
        [property: JsonPropertyName("Precio Gasoleo A")] string? PrecioGasoleoA,
        [property: JsonPropertyName("IDMunicipio")] string? IdMunicipio
    );
}
