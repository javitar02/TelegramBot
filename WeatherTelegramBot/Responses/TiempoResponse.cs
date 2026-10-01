using System.Text.Json.Serialization;

namespace WeatherTelegramBot.Responses
{
    public record TiempoResponse(
        [property: JsonPropertyName("elaborado")] string Elaborado,
        [property: JsonPropertyName("fecha")] string Fecha,
        [property: JsonPropertyName("municipio")] Municipio Municipio,
        [property: JsonPropertyName("stateSky")] StateSky EstadoCielo,
        [property: JsonPropertyName("temperatura_actual")] string TemperaturaActual,
        [property: JsonPropertyName("temperaturas")] Temperaturas Temperaturas,
        [property: JsonPropertyName("humedad")] string Humedad,
        [property: JsonPropertyName("viento")] string Viento,
        [property: JsonPropertyName("precipitacion")] string Precipitacion
    );

    public record Municipio(
        [property: JsonPropertyName("CODPROV")] string CodProvincia,
        [property: JsonPropertyName("CODIGOINE")] string CodigoIne,
        [property: JsonPropertyName("NOMBRE_PROVINCIA")] string NombreProvincia,
        [property: JsonPropertyName("NOMBRE")] string Nombre
    );

    public record StateSky(
        [property: JsonPropertyName("description")] string Descripcion,
        [property: JsonPropertyName("id")] string Id
    );

    public record Temperaturas(
        [property: JsonPropertyName("max")] string Maxima,
        [property: JsonPropertyName("min")] string Minima
    );
}