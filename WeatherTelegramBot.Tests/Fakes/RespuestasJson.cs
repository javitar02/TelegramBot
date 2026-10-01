using System.Globalization;
using System.Text.Json;
using WeatherTelegramBot.Responses;

namespace WeatherTelegramBot.Tests.Fakes;

/// <summary>
/// Cuerpos JSON con la forma exacta que publican las APIs externas, incluidos los detalles
/// que rompen un deserializador ingenuo: acentos en las claves ("Rótulo"), el espacio en
/// "Longitud (WGS84)" y los importes como texto con coma decimal.
/// </summary>
internal static class RespuestasJson
{
    private static readonly JsonSerializerOptions Opciones = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Serializar(object valor) => JsonSerializer.Serialize(valor, Opciones);

    /// <summary>El MITECO publica "1,849"; el servicio lo normaliza a punto.</summary>
    public static string Importe(double valor) => valor.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', ',');

    public static string Coordenada(double valor) => valor.ToString("0.######", CultureInfo.InvariantCulture).Replace('.', ',');

    public static EstacionServicioDto Estacion(
        string rotulo = "REPSOL",
        string direccion = "Calle Real, 1",
        string? latitud = "37,388",
        string? longitud = "-5,982",
        string? precio95 = "1,749",
        string? gasoleoA = "1,559") =>
        new(rotulo, direccion, latitud, longitud, precio95, gasoleoA);

    public static string Precios(params EstacionServicioDto[] estaciones) => Serializar(new
    {
        Fecha = "12/09/2025 08:00:00",
        ListaEESSPrecio = estaciones
    });

    public static string PreciosConFecha(string fecha, params EstacionServicioDto[] estaciones) => Serializar(new
    {
        Fecha = fecha,
        ListaEESSPrecio = estaciones
    });

    public static string PreciosSinFecha(params EstacionServicioDto[] estaciones) =>
        Serializar(new { ListaEESSPrecio = estaciones });

    public static string PreciosSinLista() => Serializar(new { Fecha = "12/09/2025 08:00:00", ListaEESSPrecio = Array.Empty<object>() });

    public static TiempoResponse Tiempo(
        string nombre = "Sevilla",
        string nombreProvincia = "Sevilla",
        string descripcion = "Despejado",
        string temperaturaActual = "28",
        string maxima = "31",
        string minima = "19",
        string humedad = "35",
        string viento = "12",
        string precipitacion = "0",
        string elaborado = "12/09/2025 10:00") =>
        new(
            elaborado,
            "2025-09-12",
            new Municipio("41", "41091", nombreProvincia, nombre),
            new StateSky(descripcion, "11"),
            temperaturaActual,
            new Temperaturas(maxima, minima),
            humedad,
            viento,
            precipitacion);

    public static string Clima(TiempoResponse tiempo) => Serializar(tiempo);

    /// <summary>
    /// Cuerpo de la predicción. Por defecto reproduce la forma "bonita" de la API: las fechas
    /// dentro de "@attributes", arrays por hora en "hoy" y bloques con maxima/minima en los
    /// días cercanos.
    /// </summary>
    public static string Prediccion(
        string nombre = "Alcalá de Guadaíra",
        string elaborado = "01/10/2026 08:00",
        bool manana = true,
        bool diasEscalares = false,
        bool sinDescripcionNiUv = false)
    {
        var hoy = Mapear(new Dictionary<string, object?>
        {
            ["@attributes"] = new Dictionary<string, object?> { ["fecha"] = "2026-10-01", ["orto"] = "08:19", ["ocaso"] = "20:07" },
            ["temperatura"] = new[] { "18", "19", "21", "24", "27", "30", "33", "32", "30", "28", "26", "24", "22", "21", "20" },
            ["sens_termica"] = new[] { "18", "19", "21", "24", "27", "30", "33", "32", "30", "28", "26", "24", "22", "21", "20" },
            ["estado_cielo_descripcion"] = sinDescripcionNiUv
                ? new[] { "11", "11n", "12", "12", "14", "14", "15", "15" }
                : new[]
                {
                    "Despejado", "Despejado", "Poco nuboso", "Poco nuboso", "Nuboso", "Nuboso",
                    "Muy nuboso", "Muy nuboso", "Nuboso", "Nuboso", "Poco nuboso", "Poco nuboso",
                    "Despejado", "Despejado", "Despejado", "Despejado"
                },
            ["prob_precipitacion"] = new[] { "5", "0", "0" },
            ["viento"] = new[]
            {
                Viento("09", "NE", "7"), Viento("10", "NE", "10"), Viento("11", "NE", "9"),
                Viento("12", "E", "5"), Viento("13", "E", "4"), Viento("14", "SE", "6"),
                Viento("15", "S", "8"), Viento("16", "S", "10"), Viento("17", "S", "12"),
                Viento("18", "S", "16"), Viento("19", "SO", "14"), Viento("20", "SO", "10"),
                Viento("21", "SO", "8"), Viento("22", "SO", "11"), Viento("23", "SO", "14")
            },
            ["racha_max"] = new[] { "15", "16", "14", "10", "9", "12", "15", "18", "20", "24", "22", "18", "16", "18", "20" }
        });

        var mananaBloque = manana
            ? Mapear(new Dictionary<string, object?>
            {
                ["@attributes"] = new Dictionary<string, object?> { ["fecha"] = "2026-10-02" },
                ["temperatura"] = new[] { "19", "20", "22", "25", "29", "33", "34", "32" },
                ["estado_cielo_descripcion"] = new[] { "Despejado", "Poco nuboso", "Muy nuboso con lluvia escasa" },
                ["prob_precipitacion"] = new[] { "80", "0" },
                ["viento"] = new[] { Viento("09", "S", "10") },
                ["racha_max"] = new[] { "18" }
            })
            // Sin el bloque "manana" la API lo omite del todo.
            : null;

        var pronostico = Mapear(new Dictionary<string, object?> { ["hoy"] = hoy, ["manana"] = mananaBloque });

        var mananaProximos = Mapear(new Dictionary<string, object?>
        {
            ["@attributes"] = new Dictionary<string, object?> { ["fecha"] = "2026-10-02" },
            ["temperatura"] = new Dictionary<string, object?> { ["maxima"] = "35", ["minima"] = "19" },
            ["sens_termica"] = new Dictionary<string, object?> { ["max"] = "34", ["min"] = "19" },
            ["humedad_relativa"] = new Dictionary<string, object?> { ["max"] = "85", ["min"] = "35" },
            ["estado_cielo_descripcion"] = new[] { "Intervalos nubosos con lluvia escasa", "Poco nuboso", "Nuboso" },
            ["prob_precipitacion"] = new[] { "80", "0", "80" },
            ["viento"] = new[] { Viento("00-24", "S", "5"), Viento("00-12", "S", "10") },
            ["racha_max"] = new[] { "", "" },
            ["uv_max"] = "5"
        });

        // El MITECO cambia de forma a mitad de semana: el tercer día pierde los arrays y
        // publica escalares, y el cuarto ya no informa del índice UV.
        var lejanoEscalares = Mapear(new Dictionary<string, object?>
        {
            ["@attributes"] = new Dictionary<string, object?> { ["fecha"] = "2026-10-03" },
            ["temperatura"] = "27",
            ["estado_cielo_descripcion"] = "Intervalos nubosos",
            ["prob_precipitacion"] = "45",
            ["viento"] = new Dictionary<string, object?> { ["direccion"] = "E", ["velocidad"] = "10" }
        });

        var lejano = Mapear(new Dictionary<string, object?>
        {
            ["@attributes"] = new Dictionary<string, object?> { ["fecha"] = "2026-10-03" },
            ["temperatura"] = new Dictionary<string, object?> { ["maxima"] = "34", ["minima"] = "21" },
            ["sens_termica"] = new Dictionary<string, object?> { ["max"] = "34", ["min"] = "21" },
            ["humedad_relativa"] = new Dictionary<string, object?> { ["max"] = "90", ["min"] = "35" },
            ["estado_cielo_descripcion"] = sinDescripcionNiUv ? "43" : new[] { "Nubes altas", "Intervalos nubosos" },
            ["prob_precipitacion"] = new[] { "70", "0", "65" },
            ["viento"] = new[] { Viento("00-24", "E", "10") },
            ["racha_max"] = new[] { "" },
            ["uv_max"] = sinDescripcionNiUv ? null : "5"
        });

        return Serializar(new Dictionary<string, object?>
        {
            ["elaborado"] = elaborado,
            ["municipio"] = new Dictionary<string, object?>
            {
                ["CODPROV"] = "41",
                ["CODIGOINE"] = "41004000000",
                ["NOMBRE_PROVINCIA"] = "Sevilla",
                ["NOMBRE"] = nombre
            },
            ["fecha"] = "2026-10-01",
            ["stateSky"] = new Dictionary<string, object?> { ["description"] = "Nuboso", ["id"] = "14" },
            ["temperatura_actual"] = "29",
            ["temperaturas"] = new Dictionary<string, object?> { ["max"] = "33", ["min"] = "18" },
            ["humedad"] = "45",
            ["viento"] = "6",
            ["precipitacion"] = "0",
            ["pronostico"] = pronostico,
            ["proximos_dias"] = new[] { mananaProximos, diasEscalares ? lejanoEscalares : lejano }
        });
    }

    /// <summary>
    /// El feed publica "@attributes" como clave real, así que hace falta un diccionario:
    /// un objeto anónimo en C# la escribiría como "attributes".
    /// </summary>
    private static Dictionary<string, object?> Viento(string periodo, string direccion, string velocidad) => new()
    {
        ["@attributes"] = new Dictionary<string, object?> { ["periodo"] = periodo },
        ["direccion"] = direccion,
        ["velocidad"] = velocidad
    };

    /// <summary>Convierte un diccionario en otro con las claves en orden, para un JSON legible.</summary>
    private static Dictionary<string, object?> Mapear(Dictionary<string, object?> origen) =>
        origen.OrderBy(par => par.Key, StringComparer.Ordinal)
            .ToDictionary(par => par.Key, par => par.Value, StringComparer.Ordinal);
}
