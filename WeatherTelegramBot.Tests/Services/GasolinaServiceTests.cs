using System.Net;
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

        await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Equal(
            "https://sedeaplicaciones.minetur.gob.es/ServiciosRESTCarburantes/PreciosCarburantes/EstacionesTerrestres/FiltroProvincia/41",
            Assert.Single(factory.Urls(FakeHttpClientFactory.ClienteGasolina)));
    }

    [Fact]
    public async Task ParseaLosImportesConComaDecimalQuePublicaElMiteco()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(RespuestasJson.Estacion(precio95: "1,749"))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Equal(1.749, Assert.Single(resultado.Gasolineras).PrecioGasolina95, 3);
    }

    [Fact]
    public async Task ExponeLaFechaDeLosDatosDelMinisterio()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.PreciosConFecha("12/09/2025 08:00:00", RespuestasJson.Estacion())));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Equal("12/09/2025 08:00:00", resultado.Actualizado);
    }

    [Fact]
    public async Task OrdenaDeMasBarataAMasCaraYDesempataPorDistancia()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(
                RespuestasJson.Estacion("CARO", precio95: "1,900", longitud: "-5,982"),
                RespuestasJson.Estacion("BARATO", precio95: "1,700", longitud: "-5,982"),
                RespuestasJson.Estacion("IGUAL", precio95: "1,700", longitud: "-5,980"))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Equal(["BARATO", "IGUAL", "CARO"], resultado.Gasolineras.Select(g => g.Nombre));
    }

    [Fact]
    public async Task DescartaLoQueQuedaFueraDelRadioIndicado()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(
                RespuestasJson.Estacion("CERCA", longitud: "-5,982"),
                RespuestasJson.Estacion("LEJOS", longitud: "-6,500"))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Equal("CERCA", Assert.Single(resultado.Gasolineras).Nombre);
    }

    [Fact]
    public async Task ExcluyeLasEstacionesSinPrecioDeGasolina95Publicado()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(
                RespuestasJson.Estacion("CON_PRECIO"),
                RespuestasJson.Estacion("SIN_PRECIO", precio95: null),
                RespuestasJson.Estacion("PRECIO_VACIO", precio95: ""))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Equal("CON_PRECIO", Assert.Single(resultado.Gasolineras).Nombre);
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

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Equal("BIEN", Assert.Single(resultado.Gasolineras).Nombre);
    }

    [Fact]
    public async Task DeduplicaLasEstacionesRepetidasEnElFeed()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(
                RespuestasJson.Estacion("REPSOL", longitud: "-5,982"),
                RespuestasJson.Estacion("REPSOL", longitud: "-5,982"))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Single(resultado.Gasolineras);
    }

    [Fact]
    public async Task NormalizaLosCamposDeTextoAusentes()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(
                new Responses.EstacionServicioDto(null, null, null, null, "37,388", "-5,982", "1,749", null, null, null, null))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        var gasolinera = Assert.Single(resultado.Gasolineras);
        Assert.Equal("Sin rótulo", gasolinera.Nombre);
        Assert.Equal("", gasolinera.Direccion);
        Assert.Equal("", gasolinera.Municipio);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DevuelvePreciosOpcionalesEnNuloCuandoNoVienen(string? bruto)
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Json(RespuestasJson.Precios(RespuestasJson.Estacion(precio98: bruto, gasoleoA: bruto))));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        var gasolinera = Assert.Single(resultado.Gasolineras);
        Assert.Null(gasolinera.PrecioGasolina98);
        Assert.Null(gasolinera.PrecioGasoleoA);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task DevuelveListaVaciaSiElMinisterioRespondeConError(HttpStatusCode codigo)
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(FakeHttpMessageHandler.Estado(codigo));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Empty(resultado.Gasolineras);
        Assert.Null(resultado.Actualizado);
    }

    [Fact]
    public async Task DevuelveListaVaciaSiElListadoVieneVacio()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(FakeHttpMessageHandler.Json(RespuestasJson.PreciosSinLista()));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Empty(resultado.Gasolineras);
    }

    [Fact]
    public async Task DevuelveListaVaciaSiElJsonEsInvalido()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(FakeHttpMessageHandler.Json("{ esto no es json"));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Empty(resultado.Gasolineras);
    }

    [Fact]
    public async Task DevuelveListaVaciaSiFallaLaRed()
    {
        var factory = new FakeHttpClientFactory().RegistrarGasolina(
            FakeHttpMessageHandler.Falla(new HttpRequestException("sin conexión")));

        var resultado = await Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Empty(resultado.Gasolineras);
    }

    [Fact]
    public async Task ReutilizaLaCacheEntreLlamadasParaNoRepetirPeticion()
    {
        var handler = FakeHttpMessageHandler.Json(RespuestasJson.Precios(RespuestasJson.Estacion()));
        var factory = new FakeHttpClientFactory().RegistrarGasolina(handler);
        var servicio = Crear(factory);

        await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);
        await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);
        await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

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

        await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);
        handler.Responder = _ => RespuestasHttp.Ok(RespuestasJson.Precios(RespuestasJson.Estacion("NUEVA")));
        var resultado = await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Equal(1, factory.Peticiones(FakeHttpClientFactory.ClienteGasolina));
        Assert.Equal("CACHEADA", Assert.Single(resultado.Gasolineras).Nombre);
    }

    [Fact]
    public async Task ReintentaEnLaSiguientePeticionSiLaCargaFallo()
    {
        // Un fallo no debe quedar cacheado: si no, el bot se quedaría sin datos media hora.
        var handler = FakeHttpMessageHandler.Estado(HttpStatusCode.ServiceUnavailable);
        var factory = new FakeHttpClientFactory().RegistrarGasolina(handler);
        var servicio = Crear(factory);

        Assert.Empty((await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10)).Gasolineras);

        handler.Responder = _ => RespuestasHttp.Ok(RespuestasJson.Precios(RespuestasJson.Estacion("RECUPERADA")));
        var recuperada = await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);

        Assert.Equal("RECUPERADA", Assert.Single(recuperada.Gasolineras).Nombre);
    }

    [Fact]
    public async Task NoComparteCacheEntreProvinciasDistintas()
    {
        var handler = FakeHttpMessageHandler.Json(RespuestasJson.Precios(RespuestasJson.Estacion()));
        var factory = new FakeHttpClientFactory().RegistrarGasolina(handler);
        var servicio = Crear(factory);

        await servicio.ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10);
        await servicio.ObtenerGasolinerasCercaAsync("11", 37.388, -5.982, 10);

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
            () => Crear(factory).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, cts.Token));
    }
}
