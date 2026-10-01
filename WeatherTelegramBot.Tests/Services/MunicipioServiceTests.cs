using WeatherTelegramBot.Services;
using WeatherTelegramBot.Tests.Fakes;

namespace WeatherTelegramBot.Tests.Services;

public class MunicipioServiceTests
{
    private static MunicipioService Crear(FakeHttpClientFactory factory) =>
        new(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<MunicipioService>.Instance);

    [Fact]
    public async Task ConsultaElCatalogoDeLaProvinciaDeSevilla()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Json(RespuestasJson.Municipios(RespuestasJson.Municipio())));

        await Crear(factory).ObtenerMunicipiosAsync();

        Assert.Equal(
            "https://api.el-tiempo.net/json/v3/provincias/41/municipios",
            Assert.Single(factory.Urls(FakeHttpClientFactory.ClienteElTiempo)));
    }

    [Fact]
    public async Task DeserializaLosCamposDelCatalogo()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Json(RespuestasJson.Municipios(
                RespuestasJson.Municipio(codigoIne: "410910001", nombre: "Sevilla", poblacion: 684234, latitud: 37.388, longitud: -5.982))));

        var municipio = Assert.Single(await Crear(factory).ObtenerMunicipiosAsync());

        Assert.Equal("410910001", municipio.CodigoIne);
        Assert.Equal("Sevilla", municipio.Nombre);
        Assert.Equal(684234, municipio.Poblacion);
        Assert.Equal(37.388, municipio.Latitud!.Value, 3);
        Assert.Equal(-5.982, municipio.Longitud!.Value, 3);
    }

    [Fact]
    public async Task OrdenaAlfabeticamenteIgnorandoMayusculasYAcentos()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Json(RespuestasJson.Municipios(
                RespuestasJson.Municipio(codigoIne: "4103", nombre: "otomínez"),
                RespuestasJson.Municipio(codigoIne: "4101", nombre: "Alájar"),
                RespuestasJson.Municipio(codigoIne: "4102", nombre: "Écija"))));

        var nombres = (await Crear(factory).ObtenerMunicipiosAsync()).Select(m => m.Nombre);

        Assert.Equal(["Alájar", "Écija", "otomínez"], nombres);
    }

    [Fact]
    public async Task ConservaLosMunicipiosSinCoordenadasConNull()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Json(RespuestasJson.Municipios(
                RespuestasJson.Municipio(nombre: "SinCoordenadas", latitud: null, longitud: null))));

        var municipio = Assert.Single(await Crear(factory).ObtenerMunicipiosAsync());

        Assert.Null(municipio.Latitud);
        Assert.Null(municipio.Longitud);
    }

    [Fact]
    public async Task CacheaElCatalogoYNoRepiteLaPeticion()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Json(RespuestasJson.Municipios(RespuestasJson.Municipio())));
        var servicio = Crear(factory);

        var primera = await servicio.ObtenerMunicipiosAsync();
        var segunda = await servicio.ObtenerMunicipiosAsync();

        Assert.Equal(1, factory.Peticiones(FakeHttpClientFactory.ClienteElTiempo));
        Assert.Same(primera, segunda);
    }

    [Fact]
    public async Task DevuelveListaVaciaSiElCatalogoLlegaVacio()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Json(RespuestasJson.MunicipiosVacio()));

        Assert.Empty(await Crear(factory).ObtenerMunicipiosAsync());
    }

    [Fact]
    public async Task DevuelveListaVaciaSiElJsonEsInvalido()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Json("[[["));

        Assert.Empty(await Crear(factory).ObtenerMunicipiosAsync());
    }

    [Fact]
    public async Task DevuelveListaVaciaSiFallaLaRed()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Falla(new HttpRequestException("sin conexión")));

        Assert.Empty(await Crear(factory).ObtenerMunicipiosAsync());
    }

    [Fact]
    public async Task PropagaLaCancelacion()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Json(RespuestasJson.Municipios(RespuestasJson.Municipio())));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Crear(factory).ObtenerMunicipiosAsync(cts.Token));
    }
}
