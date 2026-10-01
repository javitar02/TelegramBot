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
        string municipio = "Sevilla",
        string provincia = "SEVILLA",
        string? latitud = "37,388",
        string? longitud = "-5,982",
        string? precio95 = "1,749",
        string? precio98 = "1,899",
        string? gasoleoA = "1,559",
        string? horario = "24H",
        string? ideess = "AID-1") =>
        new(rotulo, direccion, municipio, provincia, latitud, longitud, precio95, precio98, gasoleoA, horario, ideess);

    public static string Precios(params EstacionServicioDto[] estaciones) => Serializar(new
    {
        Fecha = "12/09/2025 08:00:00",
        ListaEESSPrecio = estaciones,
        Nota = "Precio en EUR por litro"
    });

    public static string PreciosConFecha(string fecha, params EstacionServicioDto[] estaciones) => Serializar(new
    {
        Fecha = fecha,
        ListaEESSPrecio = estaciones,
        Nota = "Precio en EUR por litro"
    });

    public static string PreciosSinFecha(params EstacionServicioDto[] estaciones) =>
        Serializar(new { ListaEESSPrecio = estaciones });

    public static string PreciosSinLista() => Serializar(new { Fecha = "12/09/2025 08:00:00", ListaEESSPrecio = Array.Empty<object>() });

    public static MunicipioCatalogo Municipio(
        string codigoIne = "410910001",
        string codProv = "41",
        string nombre = "Sevilla",
        string nombreProvincia = "Sevilla",
        int? poblacion = 684234,
        double? latitud = 37.388,
        double? longitud = -5.982) =>
        new(codigoIne, codProv, nombreProvincia, nombre, poblacion, latitud, longitud);

    public static string Municipios(params MunicipioCatalogo[] municipios) => Serializar(new
    {
        provincia = "Sevilla",
        codprov = "41",
        municipios
    });

    public static string MunicipiosVacio() => Serializar(new
    {
        provincia = "Sevilla",
        codprov = "41",
        municipios = Array.Empty<MunicipioCatalogo>()
    });

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
}
