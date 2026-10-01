using NSubstitute;
using Telegram.Bot.Types.Enums;
using WeatherTelegramBot.Models;
using WeatherTelegramBot.Services;
using WeatherTelegramBot.Tests.Fakes;

namespace WeatherTelegramBot.Tests.Bot;

public class ActualizacionesTests
{
    private const long ChatId = 4242;

    [Theory]
    [InlineData("/start")]
    [InlineData("/START")]
    [InlineData("  /start  ")]
    public async Task SaludaYOfreceElMenuCuandoRecibeStart(string texto)
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, texto), default);

        var enviada = Assert.Single(bot.Cliente.DeMetodo("sendMessage"));
        Assert.Equal(ChatId, enviada.ChatId);
        Assert.Contains("Soy tu bot del tiempo", enviada.Texto);
        Assert.Equal(["ir_clima", "ir_gasofa"], enviada.Callbacks);
    }

    [Fact]
    public async Task MuestraElTecladoDeMunicipiosCuandoPulsanElBotonDeClima()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, "☀️ Consultar Clima"), default);

        var enviada = bot.UltimoSend;
        Assert.Equal(ChatId, enviada.ChatId);
        Assert.Equal(ParseMode.Markdown, enviada.ModoParseo);
        Assert.Contains("Municipios de Sevilla", enviada.Texto);
    }

    [Fact]
    public async Task MuestraElTecladoDeMunicipiosCuandoPulsanElBotonDeGasofa()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, "⛽ Consultar Gasofa"), default);

        var enviada = bot.UltimoSend;
        Assert.Equal(ChatId, enviada.ChatId);
        Assert.Equal(ParseMode.Markdown, enviada.ModoParseo);
        Assert.Contains("Gasolineras en Sevilla", enviada.Texto);
    }

    [Fact]
    public async Task ElBotonDeGasofaAnunciaElOrdenPorPrecio()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, "⛽ Consultar Gasofa"), default);

        Assert.Contains("de más barata a más cara", bot.UltimoSend.Texto);
    }

    [Theory]
    [InlineData("hola")]
    [InlineData("/clima")]
    [InlineData("qwerty")]
    public async Task RespondeQueNoEntiendeCuandoElTextoNoEsreconocido(string texto)
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, texto), default);

        var enviada = Assert.Single(bot.Cliente.DeMetodo("sendMessage"));
        Assert.StartsWith("No te he entendido", enviada.Texto);
        Assert.Null(enviada.Teclado);
    }

    [Fact]
    public async Task IgnoraLosMensajesSinTexto()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.SinMensajeDeTexto(ChatId), default);

        Assert.Empty(bot.Cliente.Peticiones);
    }

    [Fact]
    public async Task NoPropagaElErrorSiElEnvioFalla()
    {
        var bot = new BotDePrueba();
        bot.Cliente.ErrorAlResponder = new InvalidOperationException("fallo de red");

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, "/start"), default);
    }

    [Fact]
    public async Task PropagaLaCancelacion()
    {
        var bot = new BotDePrueba();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, "/start"), cts.Token);
    }
}

public class CallbacksTests
{
    private const long ChatId = 4242;

    [Fact]
    public async Task RespondeQueYaEstasEnLaPaginaConElBotonIndicador()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "noop|2/3"), default);

        var respuesta = Assert.Single(bot.Cliente.DeMetodo("answerCallbackQuery"));
        Assert.Equal("cb-1", respuesta.CallbackQueryId);
        Assert.Empty(bot.Cliente.DeMetodo("editMessageText"));
    }

    [Fact]
    public async Task ElMenuVuelveALaPrimeraPaginaDeMunicipios()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "menu"), default);

        var editada = bot.UltimoEdit;
        Assert.Equal(ChatId, editada.ChatId);
        Assert.Equal(500, editada.MessageId);
        Assert.Equal(ParseMode.Markdown, editada.ModoParseo);
        Assert.Contains("página 1 de", editada.Texto);
        Assert.Contains("w|41091", editada.Callbacks);
    }

    [Theory]
    [InlineData("pg|0")]
    [InlineData("pg|3")]
    public async Task ElCallbackDePaginaDelClimaReordenaElTeclado(string datos)
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, datos), default);

        Assert.NotEmpty(bot.Cliente.DeMetodo("editMessageText"));
    }

    [Theory]
    [InlineData("gp|0")]
    [InlineData("gj|S")]
    public async Task ElCallbackDeGasofaAnunciaLasGasolineras(string datos)
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, datos), default);

        Assert.Contains("Gasolineras en Sevilla", bot.UltimoEdit.Texto);
        Assert.Contains("g|41091", bot.UltimoEdit.Callbacks);
    }

    [Fact]
    public async Task ConsultaElClimaDelMunicipioQueSePulsó()
    {
        var bot = new BotDePrueba();
        bot.Clima.ObtenerTiempoPorMunicipioAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RespuestasJson.Tiempo(nombre: "Écija"));

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "w|41054"), default);

        await bot.Clima.Received(1).ObtenerTiempoPorMunicipioAsync("41", "41054", Arg.Any<CancellationToken>());
        Assert.Contains("El tiempo en Écija", bot.UltimoEdit.Texto);
    }

    [Fact]
    public async Task MuestraLasGasolinerasDelMunicipioConSuAhorro()
    {
        var bot = new BotDePrueba();
        bot.Gasolina.ObtenerGasolinerasCercaAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoGasolineras(
            [
                GasolineraDe("REPSOL", 1.749, 0.4),
                GasolineraDe("CEPSA", 1.899, 2.1)
            ],
            "12/09/2025 08:00"));

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "g|41091"), default);

        var texto = bot.UltimoEdit.Texto;
        Assert.Contains("Gasolineras cerca de", texto);
        Assert.Contains("1,749", texto);
        Assert.Contains("1,899", texto);
        Assert.Contains("Datos MITECO", texto);
    }

    [Fact]
    public async Task AvisaSiNoHayGasolinerasCerca()
    {
        var bot = new BotDePrueba();
        bot.Gasolina.ObtenerGasolinerasCercaAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoGasolineras([], null));

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "g|41091"), default);

        Assert.Contains("No hay gasolineras", bot.UltimoEdit.Texto);
    }

    [Fact]
    public async Task AvisaSiElMunicipioNoEstaEnElCatalogo()
    {
        var bot = new BotDePrueba();
        bot.Municipios.ObtenerMunicipiosAsync(Arg.Any<CancellationToken>()).Returns([]);

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "g|99999"), default);

        Assert.Contains("No se encontró ese municipio", bot.UltimoEdit.Texto);
    }

    [Fact]
    public async Task AvisaSiFaltaElMunicipioParaConsultarLasGasolineras()
    {
        var bot = new BotDePrueba();
        bot.Municipios.ObtenerMunicipiosAsync(Arg.Any<CancellationToken>())
            .Returns([RespuestasJson.Municipio(nombre: "SinCoordenadas", latitud: null, longitud: null)]);

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "g|41091"), default);

        Assert.Contains("No tengo coordenadas", bot.UltimoEdit.Texto);
    }

    [Fact]
    public async Task IgnoraElCallbackSiNoHayMensajeAssociado()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuerySinMensaje("menu"), default);

        Assert.Empty(bot.Cliente.Peticiones);
    }

    [Fact]
    public async Task IgnoraElCallbackSiNoTraeDatos()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, null), default);

        Assert.Empty(bot.Cliente.Peticiones);
    }

    [Fact]
    public async Task IgnoraLosCallbacksQueNoReconoce()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "zzz|1"), default);

        Assert.Empty(bot.Cliente.DeMetodo("editMessageText"));
    }

    [Fact]
    public async Task ToleraQueTelegramRechaceUnMensajeNoModificado()
    {
        var bot = new BotDePrueba();
        // answerCallbackQuery también pasa por SendRequest, así que el fallo se limita
        // a editMessageText para poder comprobar que el aviso se ignora y el flujo continúa.
        bot.Cliente.ErrorEnMetodo = ("editMessageText",
            new Telegram.Bot.Exceptions.ApiRequestException("Bad Request: message is not modified", 400));

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "menu"), default);

        Assert.Single(bot.Cliente.DeMetodo("answerCallbackQuery"));
    }

    [Fact]
    public async Task PropagaElErrorSiTelegramRechazaPorOtroMotivo()
    {
        var bot = new BotDePrueba();
        bot.Cliente.ErrorEnMetodo = ("editMessageText",
            new Telegram.Bot.Exceptions.ApiRequestException("Bad Request: chat not found", 400));

        await Assert.ThrowsAsync<Telegram.Bot.Exceptions.ApiRequestException>(
            () => bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "menu"), default));
    }

    private static Gasolinera GasolineraDe(string nombre, double precio, double distancia) =>
        new(nombre, "Calle Real, 1", "Sevilla", 37.388, -5.982, distancia, precio, null, null, null);
}
