using System.Net;
using WeatherTelegramBot.Models;
using WeatherTelegramBot.Services;
using WeatherTelegramBot.Tests.Fakes;

namespace WeatherTelegramBot.Tests.Services;

public class GasolinaServiceTests
{
    private static GasolinaService Crear(FakeHttpClientFactory factory) =>
        new(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<GasolinaService>.Instance);

    [Fact]
    public async Task ConsultaLaProvinciaIndicadaAlApiDelMiteco()
    {
        var handler = FakeHttpMessageHandler.Json(RespuestasJson.Precios(RespuestasJson.Estacion()));
        var factory = new FakeHttpClientFactory().RegistrarGasolina(handler);

        await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Equal(
            "https://sedeaplicaciones.minetur.gob.es/ServiciosRESTCarburantes/PreciosCarburantes/EstacionesTerrestres/FiltroProvincia/41",
            Assert.Single(factory.Urls(FakeHttpClientFactory.ClienteGasolina)));
    }

    [Fact]
    public async Task ParseaLosImportesConComaDecimalQuePublicaElMiteco()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(RespuestasJson.Estacion(precio95: "1,749"))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Equal(1.749, Assert.Single(resultado).Precio, 3);
    }

    [Fact]
    public async Task OrdenaDeMasBarataAMasCaraYDesempataPorDistancia()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(
                RespuestasJson.Estacion("CARO", precio95: "1,900", longitud: "-5,982"),
                RespuestasJson.Estacion("BARATO", precio95: "1,700", longitud: "-5,982"),
                RespuestasJson.Estacion("IGUAL", precio95: "1,700", longitud: "-5,980"))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Equal(["BARATO", "IGUAL", "CARO"], resultado.Select(g => g.Nombre));
    }

    [Fact]
    public async Task DescartaLoQueQuedaFueraDelRadioIndicado()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(
                RespuestasJson.Estacion("CERCA", longitud: "-5,982"),
                RespuestasJson.Estacion("LEJOS", longitud: "-6,500"))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Equal("CERCA", Assert.Single(resultado).Nombre);
    }

    [Fact]
    public async Task ExcluyeLasEstacionesSinPrecioDeGasolina95Publicado()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(
                RespuestasJson.Estacion("CON_PRECIO"),
                RespuestasJson.Estacion("SIN_PRECIO", precio95: null),
                RespuestasJson.Estacion("PRECIO_VACIO", precio95: ""))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Equal("CON_PRECIO", Assert.Single(resultado).Nombre);
    }

    /// <summary>El gasóleo se pide con su propia columna del feed del MITECO.</summary>
    [Fact]
    public async Task FiltraPorGasoleoACuandoSePideDiesl()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(
                RespuestasJson.Estacion("SIN_GASOLEO", gasoleoA: null),
                RespuestasJson.Estacion("CON_GASOLEO", gasoleoA: "1,559"))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.GasoleoA);

        Assert.Equal("CON_GASOLEO", Assert.Single(resultado).Nombre);
    }

    [Fact]
    public async Task ExcluyeLasEstacionesSinCoordenadasValidas()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(
                RespuestasJson.Estacion("BIEN"),
                RespuestasJson.Estacion("SIN_LATITUD", latitud: null),
                RespuestasJson.Estacion("LATITUD_INVALIDA", latitud: "N/D"),
                RespuestasJson.Estacion("SIN_LONGITUD", longitud: null))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Equal("BIEN", Assert.Single(resultado).Nombre);
    }

    [Fact]
    public async Task DeduplicaLasEstacionesRepetidasEnElFeed()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(
                RespuestasJson.Estacion("REPSOL", longitud: "-5,982"),
                RespuestasJson.Estacion("REPSOL", longitud: "-5,982"))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Single(resultado);
    }

    [Fact]
    public async Task NormalizaLosCamposDeTextoAusentes()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(
                new Responses.EstacionServicioDto(null, null, "37,388", "-5,982", "1,749", null))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        var gasolinera = Assert.Single(resultado);
        Assert.Equal("Sin rótulo", gasolinera.Nombre);
        Assert.Equal("", gasolinera.Direccion);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task DevuelveListaVaciaSiElMinisterioRespondeConError(HttpStatusCode codigo)
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(FakeHttpMessageHandler.Estado(codigo));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task DevuelveListaVaciaSiElListadoVieneVacio()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(FakeHttpMessageHandler.Json(RespuestasJson.PreciosSinLista()));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task DevuelveListaVaciaSiElJsonEsInvalido()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(FakeHttpMessageHandler.Json("{ esto no es json"));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task DevuelveListaVaciaSiFallaLaRed()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Falla(new HttpRequestException("sin conexión")));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ReutilizaLaCacheEntreLlamadasParaNoRepetirPeticion()
    {
        var handler = FakeHttpMessageHandler.Json(RespuestasJson.Precios(RespuestasJson.Estacion()));
        var factory = new FakeHttpClientFactory().RegistrarGasolina(handler);
        var servicio = Crear(factory);

        await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);
        await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);
        await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Equal(1, factory.Peticiones(FakeHttpClientFactory.ClienteGasolina));
    }

    [Fact]
    public async Task NoVuelveAConsultarMientrasLaCacheEsValida()
    {
        // La cache vive 30 minutos, así que aunque el feed cambie la segunda llamada se
        // resuelve con lo ya descargado.
        var handler = FakeHttpMessageHandler.Json(RespuestasJson.Precios(RespuestasJson.Estacion("CACHEADA")));
        var factory = new FakeHttpClientFactory().RegistrarGasolina(handler);
        var servicio = Crear(factory);

        await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);
        handler.Responder = _ => RespuestasHttp.Ok(RespuestasJson.Precios(RespuestasJson.Estacion("NUEVA")));
        var resultado = await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Equal(1, factory.Peticiones(FakeHttpClientFactory.ClienteGasolina));
        Assert.Equal("CACHEADA", Assert.Single(resultado).Nombre);
    }

    [Fact]
    public async Task ReintentaEnLaSiguientePeticionSiLaCargaFallo()
    {
        // Un fallo no debe quedar cacheado: si no, el bot se quedaría sin datos media hora.
        var handler = FakeHttpMessageHandler.Estado(HttpStatusCode.ServiceUnavailable);
        var factory = new FakeHttpClientFactory().RegistrarGasolina(handler);
        var servicio = Crear(factory);

        Assert.Empty(await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95));

        handler.Responder = _ => RespuestasHttp.Ok(RespuestasJson.Precios(RespuestasJson.Estacion("RECUPERADA")));
        var recuperada = await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Equal("RECUPERADA", Assert.Single(recuperada).Nombre);
    }

    [Fact]
    public async Task NoComparteCacheEntreProvinciasDistintas()
    {
        var handler = FakeHttpMessageHandler.Json(RespuestasJson.Precios(RespuestasJson.Estacion()));
        var factory = new FakeHttpClientFactory().RegistrarGasolina(handler);
        var servicio = Crear(factory);

        await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95);
        await servicio.ObtenerGasolinerasCercaAsync("11", 37.388, -5.982, 10, TipoCarburante.Gasolina95);

        Assert.Equal(2, factory.Peticiones(FakeHttpClientFactory.ClienteGasolina));
    }

    [Fact]
    public async Task PropagaLaCancelacionAlClienteHttp()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(RespuestasJson.Estacion())));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, TipoCarburante.Gasolina95, cts.Token));
    }
}
