using NSubstitute;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WeatherTelegramBot.Services;
using WeatherTelegramBot.Tests.Fakes;

namespace WeatherTelegramBot.Tests.Bot;

public class MenuInicioTests
{
    private const long ChatId = 4242;
    private const string CallbackClima = "w";
    private const string CallbackGasofa = "tipo";

    private static BotDePrueba Bot() => new();

    [Fact]
    public async Task StartEnviaLaBienvenidaConLosDosBotonesPrincipales()
    {
        var bot = Bot();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, "/start"), default);

        var enviada = Assert.Single(bot.Cliente.DeMetodo("sendMessage"));
        Assert.Contains("¡Hola! Soy tu bot del tiempo", enviada.Texto);
        Assert.Contains("Alcalá de Guadaíra", enviada.Texto);
        Assert.Equal(ParseMode.Markdown, enviada.ModoParseo);
        Assert.Equal([CallbackClima, CallbackGasofa], enviada.Callbacks);
    }

    /// <summary>Ya no se ofrece el clima de ningún otro municipio de la provincia.</summary>
    [Fact]
    public async Task LaBienvenidaNoHablaDeMunicipiosDeSevilla()
    {
        var bot = Bot();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, "/start"), default);

        Assert.DoesNotContain("municipios de Sevilla", bot.UltimoSend.Texto);
    }

    [Fact]
    public async Task LosBotonesDeInicioSonInlineYNoUnTecladoDeTexto()
    {
        var bot = Bot();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, "/start"), default);

        var enviada = Assert.Single(bot.Cliente.DeMetodo("sendMessage"));
        Assert.IsType<InlineKeyboardMarkup>(enviada.Teclado);
        Assert.Empty(enviada.Botones);
    }

    /// <summary>El botón de clima va directo al tiempo de Alcalá, sin listado de municipios.</summary>
    [Fact]
    public async Task ElBotonDeInicioDeClimaMuestraDirectamenteElClimaDeAlcala()
    {
        var bot = Bot();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, CallbackClima), default);

        await bot.Clima.Received(1).ObtenerTiempoPorMunicipioAsync("41", "41004", Arg.Any<CancellationToken>());
        Assert.Contains("pr", bot.UltimoEdit.Callbacks);
    }

    [Fact]
    public async Task ElBotonDeInicioDeGasofaAbreLosDosBotonesDeCarburante()
    {
        var bot = Bot();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, CallbackGasofa), default);

        Assert.Contains("¿Qué carburante quieres consultar?", bot.UltimoEdit.Texto);
        Assert.Equal(["ga|95", "ga|di"], bot.UltimoEdit.Callbacks.Take(2));
    }

    /// <summary>El menú principal se puede recuperar desde cualquier pantalla.</summary>
    [Fact]
    public async Task ElBotonDeMenuVuelveALaBienvenida()
    {
        var bot = Bot();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "menu"), default);

        Assert.Contains("¡Hola! Soy tu bot del tiempo", bot.UltimoEdit.Texto);
        Assert.Equal([CallbackClima, CallbackGasofa], bot.UltimoEdit.Callbacks);
    }

    [Fact]
    public void ElTecladoDeClimaOfrecePrediccionYLosDosBotonesPrincipales()
    {
        var teclado = TelegramBotService.ConstruirTecladoClima();

        Assert.Equal(
            [new[] { "pr" }, new[] { CallbackClima, CallbackGasofa }],
            teclado.InlineKeyboard.Select(f => f.Select(b => b.CallbackData).ToArray()).ToArray());
    }

    [Fact]
    public async Task ElListadoDeGasolinerasIncluyeLosDosBotonesPrincipales()
    {
        var bot = Bot();
        bot.Gasolina
            .ObtenerGasolinerasCercaAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<Models.TipoCarburante>(), Arg.Any<CancellationToken>())
            .Returns([
                new Models.Gasolinera("REPSOL", "Calle Real, 1", 1.749, 0.4)
            ]);

        var (_, teclado) = await bot.Bot.ConstruirTecladoGasolinerasAsync(0, Models.TipoCarburante.Gasolina95, default);

        var ultimaFila = teclado.InlineKeyboard.ToArray()[^1];
        Assert.Equal([CallbackClima, CallbackGasofa], ultimaFila.Select(b => b.CallbackData));
    }

    [Fact]
    public void ElTecladoDeAvisoTambienOfreceLosDosBotonesPrincipales()
    {
        var teclado = TelegramBotService.TecladoAviso();

        Assert.Contains(CallbackClima, teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
        Assert.Contains(CallbackGasofa, teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task SeCambiaDeConsultaSinVolverAlMenuPrincipal()
    {
        var bot = Bot();

        // Desde el teclado de clima, un toque lleva a elegir carburante.
        var (_, teclado) = await bot.Bot.ConstruirTecladoGasolinerasAsync(0, Models.TipoCarburante.Gasolina95, default);
        var botonGasofa = teclado.InlineKeyboard.ToArray()[^1].First(b => b.CallbackData == CallbackGasofa);

        await bot.Bot.HandleCallbackQueryAsync(
            BotDePrueba.CallbackQuery(ChatId, botonGasofa.CallbackData), default);

        Assert.Contains("¿Qué carburante quieres consultar?", bot.UltimoEdit.Texto);
        Assert.Single(bot.Cliente.DeMetodo("answerCallbackQuery"));
    }
}
