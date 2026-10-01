using System.Text.Json.Serialization;

namespace WeatherTelegramBot.Responses
{
    public record MunicipiosProvinciaResponse(
        [property: JsonPropertyName("provincia")] string Provincia,
        [property: JsonPropertyName("codprov")] string CodProv,
        [property: JsonPropertyName("municipios")] List<MunicipioCatalogo> Municipios
    );

    public record MunicipioCatalogo(
        [property: JsonPropertyName("CODIGOINE")] string CodigoIne,
        [property: JsonPropertyName("CODPROV")] string CodProv,
        [property: JsonPropertyName("NOMBRE_PROVINCIA")] string NombreProvincia,
        [property: JsonPropertyName("NOMBRE")] string Nombre,
        [property: JsonPropertyName("POBLACION_MUNI")] int? Poblacion,
        [property: JsonPropertyName("LATITUD_ETRS89_REGCAN95")] double? Latitud,
        [property: JsonPropertyName("LONGITUD_ETRS89_REGCAN95")] double? Longitud
    );
}