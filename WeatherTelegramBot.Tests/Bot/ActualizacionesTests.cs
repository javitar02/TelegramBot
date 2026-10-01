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
        Assert.Equal(["w", "tipo"], enviada.Callbacks);
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
    public async Task PropagaElCancelacion()
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

    private static BotDePrueba ConGasolineras(params Gasolinera[] gasolineras)
    {
        var bot = new BotDePrueba();
        bot.Gasolina
            .ObtenerGasolinerasCercaAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<TipoCarburante>(), Arg.Any<CancellationToken>())
            .Returns(gasolineras);
        return bot;
    }

    private static Gasolinera G(string nombre, double precio, double distancia) =>
        new(nombre, "Calle Real, 1", precio, distancia);

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
    public async Task ElMenuVuelveALaBienvenida()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "menu"), default);

        var editada = bot.UltimoEdit;
        Assert.Equal(ChatId, editada.ChatId);
        Assert.Equal(500, editada.MessageId);
        Assert.Equal(ParseMode.Markdown, editada.ModoParseo);
        Assert.Contains("Soy tu bot del tiempo", editada.Texto);
        Assert.Equal(["w", "tipo"], editada.Callbacks);
    }

    /// <summary>Ya no hay listado de municipios, así que su paginación antigua no se usa.</summary>
    [Theory]
    [InlineData("pg|0")]
    [InlineData("pg|3")]
    [InlineData("jp|S")]
    [InlineData("gj|S|95")]
    public async Task LosCallbacksDePaginacionAntiguosSeIgnoran(string datos)
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, datos), default);

        Assert.Empty(bot.Cliente.DeMetodo("editMessageText"));
    }

    /// <summary>Eligiendo carburante se va directo al listado, sin paso intermedio por municipio.</summary>
    [Theory]
    [InlineData("ga|95", "Gasolina 95 E5")]
    [InlineData("ga|di", "Diesel")]
    public async Task PulsarGasolinaODieselMuestraElListadoDeAlcala(string datos, string carburante)
    {
        var bot = ConGasolineras(G("REPSOL", 1.749, 0.4));

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, datos), default);

        Assert.Contains("**Gasolineras Alcalá de Guadaíra**", bot.UltimoEdit.Texto);
        Assert.Contains($"{carburante} · radio 10 km", bot.UltimoEdit.Texto);
        await bot.Gasolina.Received(1).ObtenerGasolinerasCercaAsync(
            "41", 37.463, -5.981, 10, Arg.Any<TipoCarburante>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinTokenDeCarburanteVuelveAPreguntarQueCombustible()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "ga|"), default);

        Assert.Contains("¿Qué carburante quieres consultar?", bot.UltimoEdit.Texto);
        Assert.Equal(["ga|95", "ga|di", "w", "tipo"], bot.UltimoEdit.Callbacks);
    }

    [Fact]
    public async Task ElBotonDeCambiarCarburanteAbreElSubmenu()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "tipo"), default);

        Assert.Contains("¿Qué carburante quieres consultar?", bot.UltimoEdit.Texto);
        Assert.Equal(["ga|95", "ga|di", "w", "tipo"], bot.UltimoEdit.Callbacks);
    }

    [Fact]
    public async Task ElBotonDeGasofaAnunciaElOrdenPorPrecio()
    {
        var bot = new BotDePrueba();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "tipo"), default);

        Assert.Contains("de más barata a más cara", bot.UltimoEdit.Texto);
    }

    /// <summary>
    /// El botón del clima siempre acaba en un parte, pero el dado decide de qué pueblo sale,
    /// así que el aserto ya no puede fijar el código de Alcalá.
    /// </summary>
    [Fact]
    public async Task ElCallbackDeClimaMuestraElParte()
    {
        var bot = new BotDePrueba();
        bot.Clima.ObtenerTiempoPorMunicipioAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RespuestasJson.Tiempo(nombre: "Alcalá de Guadaíra"));

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "w"), default);

        await bot.ReceivedClimaDeAlcalaOAlterno();
        Assert.Contains("El tiempo en Alcalá de Guadaíra", bot.UltimoEdit.Texto);
    }

    /// <summary>El callback antiguo con el código del municipio sigue llegando al clima.</summary>
    [Fact]
    public async Task ElCallbackAntiguoDeClimaTambienMuestraElTiempo()    {
        var bot = new BotDePrueba();
        bot.Clima.ObtenerTiempoPorMunicipioAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RespuestasJson.Tiempo(nombre: "Alcalá de Guadaíra"));

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "w|41004"), default);

        await bot.ReceivedClimaDeAlcalaOAlterno();
        Assert.Contains("El tiempo en Alcalá de Guadaíra", bot.UltimoEdit.Texto);
    }

    [Fact]
    public async Task MuestraLasGasolinerasConSusPrecios()
    {
        var bot = ConGasolineras(G("REPSOL", 1.749, 0.4), G("CEPSA", 1.899, 2.1));

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "ga|95"), default);

        var texto = bot.UltimoEdit.Texto;
        Assert.Contains("**Gasolineras Alcalá de Guadaíra**", texto);
        Assert.Contains("1,749", texto);
        Assert.Contains("1,899", texto);
        Assert.DoesNotContain("Datos MITECO", texto);
    }

    /// <summary>La paginación conserva el orden de los tokens en el callback.</summary>
    [Fact]
    public async Task LaPaginacionDelListadoMandaLaPaginaYElCarburante()
    {
        var bot = ConGasolineras(Enumerable.Range(1, 20).Select(i => G($"Estacion{i:D2}", 1.5 + i / 100.0, i)).ToArray());

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "gl|41004|1|95"), default);

        Assert.Contains("Página 2 de 2", bot.UltimoEdit.Texto);
        Assert.Contains("16. **Estacion16**", bot.UltimoEdit.Texto);
    }

    [Fact]
    public async Task ElListadoDeGasolinerasPermiteCambiarDeCarburanteYVolverAlClima()
    {
        var bot = ConGasolineras(G("REPSOL", 1.749, 0.4));

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "ga|95"), default);

        Assert.Contains("tipo", bot.UltimoEdit.Callbacks);
        Assert.Contains("w", bot.UltimoEdit.Callbacks);
    }

    [Fact]
    public async Task AvisaSiNoHayGasolinerasCerca()
    {
        var bot = ConGasolineras();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "ga|95"), default);

        Assert.Contains("No hay gasolineras", bot.UltimoEdit.Texto);
        Assert.Contains("tipo", bot.UltimoEdit.Callbacks);
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
}
