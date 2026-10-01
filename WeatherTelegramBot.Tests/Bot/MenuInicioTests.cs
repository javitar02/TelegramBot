using NSubstitute;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WeatherTelegramBot.Services;
using WeatherTelegramBot.Tests.Fakes;

namespace WeatherTelegramBot.Tests.Bot;

public class MenuInicioTests
{
    private const long ChatId = 4242;
    private const string CallbackClima = "ir_clima";
    private const string CallbackGasofa = "ir_gasofa";

    private static BotDePrueba BotConMunicipios() => new();

    [Fact]
    public async Task StartEnviaLaBienvenidaConLosDosBotonesPrincipales()
    {
        var bot = BotConMunicipios();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, "/start"), default);

        var enviada = Assert.Single(bot.Cliente.DeMetodo("sendMessage"));
        Assert.Contains("¡Hola! Soy tu bot del tiempo", enviada.Texto);
        Assert.Contains("municipios de Sevilla", enviada.Texto);
        Assert.Equal(ParseMode.Markdown, enviada.ModoParseo);
        Assert.Equal([CallbackClima, CallbackGasofa], enviada.Callbacks);
    }

    [Fact]
    public async Task LosBotonesDeInicioSonInlineYNoUnTecladoDeTexto()
    {
        var bot = BotConMunicipios();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, "/start"), default);

        var enviada = Assert.Single(bot.Cliente.DeMetodo("sendMessage"));
        Assert.IsType<InlineKeyboardMarkup>(enviada.Teclado);
        Assert.Empty(enviada.Botones);
    }

    [Fact]
    public async Task ElBotonDeInicioDeClimaAbreElListadoDeMunicipios()
    {
        var bot = BotConMunicipios();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, CallbackClima), default);

        Assert.Contains("Municipios de Sevilla", bot.UltimoEdit.Texto);
        Assert.Contains("w|41091", bot.UltimoEdit.Callbacks);
    }

    [Fact]
    public async Task ElBotonDeInicioDeGasofaAbreElListadoEnModoGasofa()
    {
        var bot = BotConMunicipios();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, CallbackGasofa), default);

        Assert.Contains("Gasolineras en Sevilla", bot.UltimoEdit.Texto);
        Assert.Contains("g|41091", bot.UltimoEdit.Callbacks);
    }

    [Fact]
    public async Task ElListadoDeMunicipiosOfreceLosDosBotonesPrincipalesAlFinal()
    {
        var bot = BotConMunicipios();

        var (_, teclado) = await bot.Bot.ConstruirTecladoMunicipiosAsync(0, false, default);

        var ultimaFila = teclado.InlineKeyboard.ToArray()[^1];
        Assert.Equal([CallbackClima, CallbackGasofa], ultimaFila.Select(b => b.CallbackData));
    }

    [Fact]
    public async Task ElListadoDeMunicipiosYaNoRepiteElBotonDeMenuPrincipal()
    {
        var bot = BotConMunicipios();

        var (_, teclado) = await bot.Bot.ConstruirTecladoMunicipiosAsync(0, false, default);

        Assert.DoesNotContain("menu", teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public void LaVistaDelClimaPermiteCambiarAClimaOGasofa()
    {
        var bot = BotConMunicipios();

        var teclado = TelegramBotService.ConstruirTecladoVolver();

        Assert.Contains(CallbackClima, teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
        Assert.Contains(CallbackGasofa, teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task ElListadoDeGasolinerasIncluyeLosDosBotonesPrincipales()
    {
        var bot = BotConMunicipios();
        bot.Gasolina
            .ObtenerGasolinerasCercaAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new Models.ResultadoGasolineras(
            [
                new Models.Gasolinera("REPSOL", "Calle Real, 1", "Sevilla", 37.388, -5.982, 0.4, 1.749, null, null, null)
            ],
            "12/09/2025 08:00"));

        var (_, teclado) = await bot.Bot.ConstruirTecladoGasolinerasAsync("41091", 0, default);

        var ultimaFila = teclado.InlineKeyboard.ToArray()[^1];
        Assert.Equal([CallbackClima, CallbackGasofa], ultimaFila.Select(b => b.CallbackData));
    }

    [Fact]
    public async Task ElTecladoDeAvisoTambienOfreceLosDosBotonesPrincipales()
    {
        var bot = BotConMunicipios();
        bot.Municipios.ObtenerMunicipiosAsync(Arg.Any<CancellationToken>()).Returns([]);

        var (_, teclado) = await bot.Bot.ConstruirTecladoGasolinerasAsync("99999", 0, default);

        Assert.Contains(CallbackClima, teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
        Assert.Contains(CallbackGasofa, teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task SeCambiaDeConsultaSinVolverAlMenuPrincipal()
    {
        var bot = BotConMunicipios();

        // Desde el listado de clima, un toque lleva al listado de gasofa.
        var (_, teclado) = await bot.Bot.ConstruirTecladoMunicipiosAsync(0, false, default);
        var botonGasofa = teclado.InlineKeyboard.ToArray()[^1].First(b => b.CallbackData == CallbackGasofa);

        await bot.Bot.HandleCallbackQueryAsync(
            BotDePrueba.CallbackQuery(ChatId, botonGasofa.CallbackData), default);

        Assert.Contains("Gasolineras en Sevilla", bot.UltimoEdit.Texto);
        Assert.Single(bot.Cliente.DeMetodo("answerCallbackQuery"));
    }

    [Fact]
    public async Task LosBotonesAntiguosDeTextoSiguenSiendoValidos()
    {
        // Los usuarios que ya tenían el reply keyboard instalado pueden seguir pulsándolo.
        var bot = BotConMunicipios();

        await bot.Bot.HandleUpdateAsync(bot.Cliente, BotDePrueba.Mensaje(ChatId, "⛽ Consultar Gasofa"), default);

        Assert.Contains("Gasolineras en Sevilla", bot.UltimoSend.Texto);
    }
}
