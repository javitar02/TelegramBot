using NSubstitute;
using WeatherTelegramBot.Models;
using WeatherTelegramBot.Services;
using WeatherTelegramBot.Tests.Fakes;

namespace WeatherTelegramBot.Tests.Bot;

public class PrediccionTests
{
    private const long ChatId = 777;

    private static PrediccionMunicipio Ejemplo() => new(
        Nombre: "Alcalá de Guadaíra",
        Elaborado: "01/10/2026 08:00",
        Horarios:
        [
            new TramoHorario("09", 18, 17, "Despejado", 5, "NE", 7),
            new TramoHorario("10", 19, 18, "Poco nuboso", 0, "NE", 10)
        ],
        Dias:
        [
            new DiaPrediccion("Alcalá de Guadaíra", new DateOnly(2026, 10, 1), "Despejado", 33, 18, 5, 7, 15, 5),
            new DiaPrediccion("Alcalá de Guadaíra", new DateOnly(2026, 10, 2), "Intervalos nubosos con lluvia escasa", 35, 19, 80, 10, 18, 5)
        ]);

    private static BotDePrueba ConPrediccion(PrediccionMunicipio? prediccion = null)
    {
        var bot = new BotDePrueba();
        bot.Prediccion
            .ObtenerPrediccionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(prediccion ?? Ejemplo());
        return bot;
    }

    [Fact]
    public async Task MuestraElTituloConElNombreDelMunicipio()
    {
        var bot = ConPrediccion();

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.Contains("**Predicción en Alcalá de Guadaíra**", texto);
    }

    [Fact]
    public async Task DesglosaElDiaEnCursoPorHoras()
    {
        var bot = ConPrediccion();

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.Contains("**Hoy por horas**", texto);
        Assert.Contains("09h Despejado · 18°C · sens. 17° · NE 7 km/h", texto);
        Assert.Contains("10h Poco nuboso · 19°C · sens. 18° · NE 10 km/h", texto);
    }

    [Fact]
    public async Task ResumeLosProximosDiasConTemperaturasYCielo()
    {
        var bot = ConPrediccion();

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.Contains("**Próximos días**", texto);
        Assert.Contains("• vie 2 oct · 19° / 35°C · Intervalos nubosos con lluvia escasa · lluvia 80% · viento 10 km/h · racha 18 km/h · UV 5", texto);
    }

    /// <summary>El día en curso ya sale en el desglose horario, así que no se repite.</summary>
    [Fact]
    public async Task NoRepiteElDiaEnCursoEnElResumen()
    {
        var bot = ConPrediccion();

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.DoesNotContain("jue 1 oct", texto);
    }

    [Fact]
    public async Task MuestraLaFechaDeElaboracion()
    {
        var bot = ConPrediccion();

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.Contains("Actualizado: 01/10/2026 08:00", texto);
    }

    /// <summary>Con un solo día no tiene sentido la sección de próximos días.</summary>
    [Fact]
    public async Task OmiteElResumenSiNoHayMasDias()
    {
        var soloHoy = Ejemplo() with { Dias = [Ejemplo().Dias[0]] };
        var bot = ConPrediccion(soloHoy);

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.Contains("**Hoy por horas**", texto);
        Assert.DoesNotContain("**Próximos días**", texto);
    }

    [Fact]
    public async Task AvisaSiNoVieneNiHorasNiDias()
    {
        var vacia = Ejemplo() with { Horarios = [], Dias = [] };
        var bot = ConPrediccion(vacia);

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.Contains("No se pudo obtener la predicción para Alcalá de Guadaíra", texto);
    }

    [Fact]
    public async Task AvisaSiElServicioDevuelveNull()
    {
        var bot = ConPrediccion(null!);
        bot.Prediccion
            .ObtenerPrediccionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((PrediccionMunicipio?)null);

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.Contains("No se pudo obtener la predicción", texto);
    }

    /// <summary>Sin cielo ni temperatura, el tramo se rotula como "Variable".</summary>
    [Fact]
    public async Task SustituyeElCieloVacioPorVariable()
    {
        var sinCielo = Ejemplo() with
        {
            Horarios = [new TramoHorario("09", null, null, "", null, null, null)]
        };
        var bot = ConPrediccion(sinCielo);

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.Contains("09h Variable", texto);
        Assert.DoesNotContain("Sin detalle horario disponible", texto);
    }

    /// <summary>La hora viene como "HH", así que solo se conserva su parte horaria.</summary>
    [Fact]
    public async Task RecortaLaHoraAToDosDigitos()
    {
        var conHoraLarga = Ejemplo() with
        {
            Horarios = [new TramoHorario("09-12", 20, null, "Despejado", null, null, null)]
        };
        var bot = ConPrediccion(conHoraLarga);

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.Contains("09h Despejado", texto);
    }

    [Fact]
    public async Task UnaHoraSinEtiquetaSeMuestraComoDesconocida()
    {
        var sinHora = Ejemplo() with
        {
            Horarios = [new TramoHorario(null, 20, null, "Despejado", null, null, null)]
        };
        var bot = ConPrediccion(sinHora);

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.Contains("--h Despejado", texto);
    }

    [Fact]
    public async Task UnDiaSinMinimaSeResumeSoloConLaMaxima()
    {
        var sinMinima = Ejemplo() with
        {
            Dias = [Ejemplo().Dias[0], Ejemplo().Dias[1] with { Minima = null }]
        };
        var bot = ConPrediccion(sinMinima);

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.Contains("• vie 2 oct · máx. 35°C", texto);
    }

    [Fact]
    public async Task UnDiaSinTemperaturasNoMuestraGrados()
    {
        var sinTemperaturas = Ejemplo() with
        {
            Dias = [Ejemplo().Dias[0], Ejemplo().Dias[1] with { Maxima = null, Minima = null }]
        };
        var bot = ConPrediccion(sinTemperaturas);

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.Contains("• vie 2 oct · Intervalos nubosos con lluvia escasa", texto);
        Assert.DoesNotContain("°", texto.Split("**Próximos días**")[1]);
    }

    [Fact]
    public async Task UnDiaSinUvNoLoMuestra()
    {
        var sinUv = Ejemplo() with
        {
            Dias = [Ejemplo().Dias[0], Ejemplo().Dias[1] with { UvMax = null }]
        };
        var bot = ConPrediccion(sinUv);

        var texto = await bot.Bot.ObtenerPrediccionAsync(default);

        Assert.DoesNotContain("UV", texto);
    }

    [Fact]
    public void EscapaLasDescripcionesParaNoRomperElMarkdown()
    {
        var conMarkdown = Ejemplo() with
        {
            Horarios = [new TramoHorario("09", 20, null, "Nubes_altas *raras*", null, null, null)]
        };
        var bot = ConPrediccion(conMarkdown);

        var texto = bot.Bot.ObtenerPrediccionAsync(default).Result;

        Assert.Contains(@"Nubes\_altas \*raras\*", texto);
    }

    [Fact]
    public async Task ElCallbackDePrediccionMuestraLaPrevisionDeAlcala()
    {
        var bot = ConPrediccion();

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "pr"), default);

        await bot.Prediccion.Received(1).ObtenerPrediccionAsync("41", "41004", Arg.Any<CancellationToken>());
        Assert.Contains("**Predicción en Alcalá de Guadaíra**", bot.UltimoEdit.Texto);
        Assert.Contains("w", bot.UltimoEdit.Callbacks);
    }

    [Fact]
    public async Task LaPantallaDeClimaDeAlcalaOfreceElBotonDePrediccion()
    {
        var bot = ConPrediccion();
        bot.Clima.ObtenerTiempoPorMunicipioAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RespuestasJson.Tiempo(nombre: "Alcalá de Guadaíra"));

        await bot.Bot.HandleCallbackQueryAsync(BotDePrueba.CallbackQuery(ChatId, "w"), default);

        Assert.Contains("pr", bot.UltimoEdit.Callbacks);
    }

    [Theory]
    [InlineData("2026-10-01", "jue 1 oct")]
    [InlineData("2026-10-03", "sáb 3 oct")]
    [InlineData("2026-10-04", "dom 4 oct")]
    public void EtiquetaLosDiasEnEspanolYEnFormatoCorto(string fecha, string esperado)
    {
        Assert.Equal(esperado, TelegramBotService.FormatoDia(DateOnly.Parse(fecha)));
    }
}
