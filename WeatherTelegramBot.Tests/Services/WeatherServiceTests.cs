using WeatherTelegramBot.Services;
using WeatherTelegramBot.Tests.Fakes;

namespace WeatherTelegramBot.Tests.Services;

public class WeatherServiceTests
{
    private static WeatherService Crear(FakeHttpClientFactory factory) =>
        new(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<WeatherService>.Instance);

    [Fact]
    public async Task ConsultaElMunicipioIndicadoDeLaProvincia()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Json(RespuestasJson.Clima(RespuestasJson.Tiempo())));

        await Crear(factory).ObtenerTiempoPorMunicipioAsync("41", "41091");

        Assert.Equal(
            "https://api.el-tiempo.net/json/v3/provincias/41/municipios/41091",
            Assert.Single(factory.Urls(FakeHttpClientFactory.ClienteElTiempo)));
    }

    [Fact]
    public async Task DeserializaLaPrediccionCompleta()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Json(RespuestasJson.Clima(RespuestasJson.Tiempo(
                nombre: "Écija",
                descripcion: "Nublado",
                temperaturaActual: "21",
                maxima: "24",
                minima: "14",
                humedad: "62",
                viento: "8",
                precipitacion: "3.4",
                elaborado: "12/09/2025 11:30"))));

        var tiempo = await Crear(factory).ObtenerTiempoPorMunicipioAsync("41", "41091");

        Assert.NotNull(tiempo);
        Assert.Equal("Écija", tiempo.Municipio.Nombre);
        Assert.Equal("Nublado", tiempo.EstadoCielo.Descripcion);
        Assert.Equal("21", tiempo.TemperaturaActual);
        Assert.Equal("24", tiempo.Temperaturas.Maxima);
        Assert.Equal("14", tiempo.Temperaturas.Minima);
        Assert.Equal("62", tiempo.Humedad);
        Assert.Equal("8", tiempo.Viento);
        Assert.Equal("3.4", tiempo.Precipitacion);
        Assert.Equal("12/09/2025 11:30", tiempo.Elaborado);
    }

    [Fact]
    public async Task DevuelveNullSiElJsonEsInvalido()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(FakeHttpMessageHandler.Json("no es json"));

        Assert.Null(await Crear(factory).ObtenerTiempoPorMunicipioAsync("41", "41091"));
    }

    [Fact]
    public async Task DevuelveNullSiElMunicipioNoExisteYResponde404()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(FakeHttpMessageHandler.Estado(System.Net.HttpStatusCode.NotFound));

        Assert.Null(await Crear(factory).ObtenerTiempoPorMunicipioAsync("41", "99999"));
    }

    [Fact]
    public async Task DevuelveNullSiFallaLaRed()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Falla(new HttpRequestException("sin conexión")));

        Assert.Null(await Crear(factory).ObtenerTiempoPorMunicipioAsync("41", "41091"));
    }

    [Fact]
    public async Task PropagaLaCancelacion()
    {
        var factory = new FakeHttpClientFactory().RegistrarElTiempo(
            FakeHttpMessageHandler.Json(RespuestasJson.Clima(RespuestasJson.Tiempo())));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Crear(factory).ObtenerTiempoPorMunicipioAsync("41", "41091", cts.Token));
    }
}
