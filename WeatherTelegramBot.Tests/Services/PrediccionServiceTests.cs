using System.Net;
using NSubstitute;
using WeatherTelegramBot.Models;
using WeatherTelegramBot.Services;
using WeatherTelegramBot.Tests.Fakes;

namespace WeatherTelegramBot.Tests.Services;

public class PrediccionServiceTests
{
    private static PrediccionService Crear(FakeHttpClientFactory factory) =>
        new(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<PrediccionService>.Instance);

    private static FakeHttpClientFactory Con(string cuerpo) =>
        new FakeHttpClientFactory().RegistrarElTiempo(FakeHttpMessageHandler.Json(cuerpo));

    private static FakeHttpClientFactory ConError(System.Net.HttpStatusCode codigo) =>
        new FakeHttpClientFactory().RegistrarElTiempo(FakeHttpMessageHandler.Estado(codigo));

    private static FakeHttpClientFactory ConFalloDeRed() =>
        new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Falla(new HttpRequestException("sin conexión")));

    [Fact]
    public async Task ConsultaLaProvinciaYMunicipioIndicados()
    {
        var factory = Con(RespuestasJson.Prediccion());

        await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        Assert.Equal(
            "https://api.el-tiempo.net/json/v3/provincias/41/municipios/41004",
            Assert.Single(factory.Urls(FakeHttpClientFactory.ClienteElTiempo)));
    }

    [Fact]
    public async Task LeeElNombreDelMunicipioYLaFechaDeElaboracion()
    {
        var factory = Con(RespuestasJson.Prediccion(nombre: "Alcalá de Guadaíra", elaborado: "01/10/2026 08:00"));

        var prediccion = await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        Assert.Equal("Alcalá de Guadaíra", prediccion!.Nombre);
        Assert.Equal("01/10/2026 08:00", prediccion.Elaborado);
    }

    [Fact]
    public async Task DesglosaElDiaActualPorHoras()
    {
        var factory = Con(RespuestasJson.Prediccion());

        var prediccion = await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        Assert.Equal(15, prediccion!.Horarios.Count);
        Assert.Equal("09", prediccion.Horarios[0].Hora);
        Assert.Equal("Despejado", prediccion.Horarios[0].Cielo);
        Assert.Equal(18, prediccion.Horarios[0].Temperatura);
        Assert.Equal("NE", prediccion.Horarios[0].VientoDireccion);
        Assert.Equal(7, prediccion.Horarios[0].VientoVelocidad);
    }

    [Fact]
    public async Task ResumeLosProximosDiasConMaximaMinimaYCielo()
    {
        var factory = Con(RespuestasJson.Prediccion());

        var prediccion = await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        Assert.Equal(["2026-10-01", "2026-10-02", "2026-10-03"], prediccion!.Dias.Select(d => d.Fecha.ToString("yyyy-MM-dd")));
        Assert.Equal(33, prediccion.Dias[0].Maxima);
        Assert.Equal(18, prediccion.Dias[0].Minima);
        Assert.Equal(35, prediccion.Dias[1].Maxima);
        Assert.Equal(5, prediccion.Dias[1].UvMax);
    }

    /// <summary>
    /// El listado de la API empieza en mañana y el bloque "manana" también cubre ese día:
    /// sin descartar el duplicado, la lista de días empezaría por una fecha repetida.
    /// </summary>
    [Fact]
    public async Task NoRepiteElDiaDeMananaQueYaSaleDelResumen()
    {
        var factory = Con(RespuestasJson.Prediccion());

        var prediccion = await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        Assert.Single(prediccion!.Dias, d => d.Fecha == new DateOnly(2026, 10, 2));
    }

    [Fact]
    public async Task AceptaLosCamposComoEscalarCuandoElFeedDegrada()
    {
        var factory = Con(RespuestasJson.Prediccion(diasEscalares: true));

        var prediccion = await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        // Sin max/min ni arrays por tramo se deriva lo que se pueda sin romper el resto.
        var lejano = Assert.Single(prediccion!.Dias, d => d.Fecha == new DateOnly(2026, 10, 3));
        Assert.Equal(27, lejano.Maxima);
        Assert.Equal("Intervalos nubosos", lejano.Cielo);
        Assert.Equal(45, lejano.ProbPrecipitacion);
        Assert.Equal(10, lejano.Viento);
        Assert.Equal(15, prediccion.Horarios.Count);
    }

    /// <summary>
    /// La API real no publica la fecha en la raíz del día, sino dentro de "@attributes". Sin
    /// leerla de ahí la lista de días salía vacía.
    /// </summary>
    [Fact]
    public async Task LeeLaFechaDelAtributosCuandoNoEstaEnLaRaiz()
    {
        var factory = Con(FeedRealMinimo);

        var prediccion = await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        Assert.Equal(
            ["2026-10-01", "2026-10-02", "2026-10-03"],
            prediccion!.Dias.Select(d => d.Fecha.ToString("yyyy-MM-dd")));
    }

    /// <summary>
    /// La velocidad del viento va anidada en "velocidad" dentro de cada tramo, así que no se
    /// puede tomar el máximo del array directamente.
    /// </summary>
    [Fact]
    public async Task TomaLaRachaMasAltaCuandoElVientoVieneComoArrayDeObjetos()
    {
        var factory = Con(FeedRealMinimo);

        var prediccion = await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        var manana = Assert.Single(prediccion!.Dias, d => d.Fecha == new DateOnly(2026, 10, 2));
        Assert.Equal(10, manana.Viento);
    }

    /// <summary>Los días lejanos traen el viento como un único objeto, no como array.</summary>
    [Fact]
    public async Task LeeElVientoCuandoElDiaLoTraeComoUnicoObjeto()
    {
        var factory = Con(FeedRealMinimo);

        var prediccion = await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        var lejano = Assert.Single(prediccion!.Dias, d => d.Fecha == new DateOnly(2026, 10, 3));
        Assert.Equal(14, lejano.Viento);
    }

    /// <summary>
    /// La API real nombra los extremos "maxima" y "minima", no "max" y "min".
    /// </summary>
    [Fact]
    public async Task LeeLaMaximaYLaMinimaConLosNombresDelFeedReal()
    {
        var factory = Con(FeedRealMinimo);

        var prediccion = await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        var manana = Assert.Single(prediccion!.Dias, d => d.Fecha == new DateOnly(2026, 10, 2));
        Assert.Equal(34, manana.Maxima);
        Assert.Equal(19, manana.Minima);
    }

    /// <summary>Recorta de un feed real de el-tiempo.net, con las fechas en "@attributes".</summary>
    private const string FeedRealMinimo = """
        {
          "elaborado": "01/10/2026 08:00",
          "municipio": {
            "CODPROV": "41",
            "CODIGOINE": "41004000000",
            "NOMBRE_PROVINCIA": "Sevilla",
            "NOMBRE": "Alcalá de Guadaíra"
          },
          "pronostico": {
            "hoy": {
              "@attributes": { "fecha": "2026-10-01" },
              "temperatura": ["18", "24"],
              "sens_termica": ["18", "24"],
              "estado_cielo_descripcion": ["Despejado", "Poco nuboso"],
              "prob_precipitacion": ["5", "0"],
              "viento": [
                { "@attributes": { "periodo": "09" }, "direccion": "NE", "velocidad": "7" },
                { "@attributes": { "periodo": "10" }, "direccion": "NE", "velocidad": "10" }
              ],
              "racha_max": ["15", "16"]
            }
          },
          "proximos_dias": [
            {
              "@attributes": { "fecha": "2026-10-02" },
              "temperatura": { "maxima": "34", "minima": "19", "dato": ["19", "32"] },
              "estado_cielo_descripcion": ["Intervalos nubosos con lluvia escasa"],
              "prob_precipitacion": ["80", "0"],
              "viento": [
                { "@attributes": { "periodo": "00-24" }, "direccion": "S", "velocidad": "10" },
                { "@attributes": { "periodo": "00-12" }, "direccion": "S", "velocidad": "6" }
              ],
              "racha_max": [""],
              "uv_max": "5"
            },
            {
              "@attributes": { "fecha": "2026-10-03" },
              "temperatura": { "maxima": "34", "minima": "21" },
              "estado_cielo_descripcion": ["Nubes altas"],
              "prob_precipitacion": ["70"],
              "viento": { "direccion": "E", "velocidad": "14" }
            }
          ]
        }
        """;

    [Fact]
    public async Task TraduceLosCodigosDeCieloCuandoNoVieneDescripcion()
    {
        var factory = Con(RespuestasJson.Prediccion(sinDescripcionNiUv: true));

        var prediccion = await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        Assert.Equal("Intervalos nubosos con lluvia escasa", Assert.Single(prediccion!.Dias, d => d.Fecha == new DateOnly(2026, 10, 3)).Cielo);
        Assert.Null(Assert.Single(prediccion.Dias, d => d.Fecha == new DateOnly(2026, 10, 3)).UvMax);
    }

    [Fact]
    public async Task TomaLaMayorProbabilidadDeLluviaDelDia()
    {
        var factory = Con(RespuestasJson.Prediccion());

        var prediccion = await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        // El día 2026-10-02 trae tramos 80/0/80: el usuario quiere el peor caso.
        Assert.Equal(80, prediccion!.Dias[1].ProbPrecipitacion);
    }

    [Fact]
    public async Task ToleraQueNoVengaElBloqueDeManana()
    {
        var factory = Con(RespuestasJson.Prediccion(manana: false));

        var prediccion = await Crear(factory).ObtenerPrediccionAsync("41", "41004");

        Assert.NotNull(prediccion);
        Assert.NotEmpty(prediccion!.Horarios);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task DevuelveNullSiLaApiFalla(HttpStatusCode codigo)
    {
        Assert.Null(await Crear(ConError(codigo)).ObtenerPrediccionAsync("41", "41004"));
    }

    [Fact]
    public async Task DevuelveNullSiElJsonEsInvalido()
    {
        Assert.Null(await Crear(Con("{ esto no es json")).ObtenerPrediccionAsync("41", "41004"));
    }

    [Fact]
    public async Task DevuelveNullSiFallaLaRed()
    {
        Assert.Null(await Crear(ConFalloDeRed()).ObtenerPrediccionAsync("41", "41004"));
    }

    [Fact]
    public async Task PropagaLaCancelacion()
    {
        var factory = Con(RespuestasJson.Prediccion());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Crear(factory).ObtenerPrediccionAsync("41", "41004", cts.Token));
    }

    [Theory]
    [InlineData("11", "Despejado")]
    [InlineData("11n", "Despejado")]
    [InlineData("14", "Nuboso")]
    [InlineData("43", "Intervalos nubosos con lluvia escasa")]
    [InlineData("99", "Variable")]
    public void TraduceLosCodigosDeCieloConocidos(string codigo, string esperado)
    {
        Assert.Equal(esperado, PrediccionService.CieloDesdeCodigo(codigo));
    }
}