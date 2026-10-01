using NSubstitute;
using Telegram.Bot.Types.Enums;
using WeatherTelegramBot.Models;
using WeatherTelegramBot.Services;
using WeatherTelegramBot.Tests.Fakes;

namespace WeatherTelegramBot.Tests.Bot;

public class TextoClimaTests
{
    [Fact]
    public async Task ResumeLaPrediccionConTodosLosCampos()
    {
        var bot = new BotDePrueba();
        bot.Clima.ObtenerTiempoPorMunicipioAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RespuestasJson.Tiempo(
                nombre: "Alcalá de Guadaíra",
                nombreProvincia: "Sevilla",
                descripcion: "Despejado",
                temperaturaActual: "28",
                maxima: "31",
                minima: "19",
                humedad: "35",
                viento: "12",
                precipitacion: "0",
                elaborado: "12/09/2025 10:00"));

        var texto = await bot.Bot.ObtenerTiempoAsync("41", "41004", default);

        Assert.Contains("**El tiempo en Alcalá de Guadaíra** (Sevilla)", texto);
        Assert.Contains("Estado: Despejado", texto);
        Assert.Contains("Actual: 28°C | Mín: 19°C | Máx: 31°C", texto);
        Assert.Contains("Humedad: 35% | Viento: 12 km/h", texto);
        Assert.Contains("Precipitación: 0 mm", texto);
        Assert.Contains("Actualizado: 12/09/2025 10:00", texto);
    }

    [Fact]
    public async Task AvisaSiElServicioDevuelveNull()
    {
        var bot = new BotDePrueba();
        bot.Clima.ObtenerTiempoPorMunicipioAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Responses.TiempoResponse?)null);

        var texto = await bot.Bot.ObtenerTiempoAsync("41", "41004", default);

        Assert.Contains("No se pudo obtener la información", texto);
    }

    /// <summary>
    /// El municipio se decide en la sobrecarga sin argumentos, así que aquí se comprueba que
    /// el render pide exactamente el que se le pasa.
    /// </summary>
    [Fact]
    public async Task ConsultaElClimaDelMunicipioQueSeLePide()
    {
        var bot = new BotDePrueba();

        await bot.Bot.ObtenerTiempoAsync("41", "41004", default);
        await bot.Bot.ObtenerTiempoAsync("23", "23039", default);

        await bot.Clima.Received(1).ObtenerTiempoPorMunicipioAsync("41", "41004", Arg.Any<CancellationToken>());
        await bot.Clima.Received(1).ObtenerTiempoPorMunicipioAsync("23", "23039", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// El municipio se decide en la sobrecarga sin argumentos, así que el render nunca elige
    /// por su cuenta: hay que comprobar que pide Alcalá o, si el dado lo saca, un pueblo de
    /// la lista de alternativos.
    /// </summary>
    [Fact]
    public async Task ElDadoSoloSacaPueblosDeLaLista()
    {
        var bot = new BotDePrueba();

        for (int tirada = 0; tirada < 40; tirada++)
        {
            bot.Clima.ClearReceivedCalls();

            await bot.Bot.ObtenerTiempoAsync(default);

            await bot.ReceivedClimaDeAlcalaOAlterno();
        }
    }

    [Fact]
    public async Task MuestraGuarromanCuandoSePideSuMunicipio()
    {
        var bot = new BotDePrueba();
        bot.Clima.ObtenerTiempoPorMunicipioAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RespuestasJson.Tiempo(nombre: "Guarromán", nombreProvincia: "Jaén"));

        var texto = await bot.Bot.ObtenerTiempoAsync("23", "23039", default);

        Assert.Contains("**El tiempo en Guarromán** (Jaén)", texto);
    }
}

public class ConsejoCocheTests
{
    private static async Task<string> ParteCon(string descripcion, string precipitacion)
    {
        var bot = new BotDePrueba();
        bot.Clima.ObtenerTiempoPorMunicipioAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RespuestasJson.Tiempo(descripcion: descripcion, precipitacion: precipitacion));

        return await bot.Bot.ObtenerTiempoAsync("41", "41004", default);
    }

    [Theory]
    [InlineData("Despejado")]
    [InlineData("Poco nuboso")]
    [InlineData("Muy nuboso")]
    [InlineData("Cubierto")]
    [InlineData("Niebla")]
    public async Task DiceQueLaveElCocheCuandoNoHayAguaEnElCielo(string descripcion)
    {
        var texto = await ParteCon(descripcion, "0");

        Assert.Contains("Lave el coche", texto);
        Assert.DoesNotContain("BAJO NINGÚN CONCEPTO", texto);
    }

    [Theory]
    [InlineData("Intervalos nubosos con lluvia escasa")]
    [InlineData("Nuboso con lluvia")]
    [InlineData("Muy nuboso con lluvia escasa")]
    [InlineData("Tormentas")]
    [InlineData("Nieve")]
    public async Task DiceQueNoLaveElCocheCuandoElCieloLlueve(string descripcion)
    {
        var texto = await ParteCon(descripcion, "0");

        Assert.Contains("BAJO NINGÚN CONCEPTO lave el coche", texto);
    }

    /// <summary>
    /// El MITECO publica "Cubierto con lluvia escasa" con 0,0 mm acumulados, así que con mirar
    /// los milímetros el consejo saldría al revés.
    /// </summary>
    [Fact]
    public async Task DiceQueNoLaveElCocheSiHaCaidoAguaAunqueElCieloNoLoDiga()
    {
        var texto = await ParteCon("Despejado", "0.4");

        Assert.Contains("BAJO NINGÚN CONCEPTO lave el coche", texto);
    }

    [Fact]
    public async Task ElConsejoEsLaUltimaLinea()
    {
        var texto = await ParteCon("Despejado", "0");

        Assert.EndsWith("**Lave el coche**", texto);
    }
}

public class MunicipiosAlternosTests
{
    [Fact]
    public void GuarromanEstaEnLaLista()
    {
        Assert.Contains(MunicipiosAlternos.Todos, a => a.CodProvincia == "23" && a.CodIne == "23039");
    }

    /// <summary>
    /// Todos los códigos tienen que servir para la URL de el-tiempo.net: dos dígitos de
    /// provincia y cinco de INE, que es como se abrevian.
    /// </summary>
    [Fact]
    public void TodosLosCodigosTienenLaFormaDeLaUrl()
    {
        Assert.NotEmpty(MunicipiosAlternos.Todos);

        foreach (var alterno in MunicipiosAlternos.Todos)
        {
            Assert.Equal(2, alterno.CodProvincia.Length);
            Assert.Equal(5, alterno.CodIne.Length);
            Assert.All(alterno.CodProvincia, c => Assert.True(char.IsAsciiDigit(c)));
            Assert.All(alterno.CodIne, c => Assert.True(char.IsAsciiDigit(c)));
        }
    }

    /// <summary>
    /// El dado tiene que salir a la mitad de las veces. Con 1000 tiradas la desviación
    /// típica es del 1,6%, así que el margen del 10% no da falsos negativos.
    /// </summary>
    [Fact]
    public void ElDadoSaleGuarromanMasOMenosLaMitadDeLasVeces()
    {
        int alternos = 0;
        const int Tiradas = 1000;

        for (int i = 0; i < Tiradas; i++)
        {
            if (MunicipiosAlternos.Elegir() is not null)
                alternos++;
        }

        Assert.InRange(alternos, Tiradas * 4 / 10, Tiradas * 6 / 10);
    }
}

public class TecladoGasolinerasTests
{
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
    public async Task MuestraCabeceraConElRadioYElTotalDeGasolineras()
    {
        var bot = ConGasolineras(G("REPSOL", 1.749, 0.4), G("CEPSA", 1.899, 2.1));

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync(0, TipoCarburante.Gasolina95, default);

        Assert.Contains("**Gasolineras Alcalá de Guadaíra**", texto);
        Assert.Contains("Gasolina 95 E5 · radio 10 km · 2 gasolineras", texto);
    }

    /// <summary>El gasóleo se anuncia como "Diesel", que es como lo pide el usuario.</summary>
    [Fact]
    public async Task ElGasoleoSeTitulaDiesel()
    {
        var bot = ConGasolineras(G("REPSOL", 1.599, 0.4));

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync(0, TipoCarburante.GasoleoA, default);

        Assert.Contains("Diesel · radio 10 km · 1 gasolineras", texto);
    }

    [Fact]
    public async Task NumeraLasGasolinerasDesdeLaPosicionRealDeLaPagina()
    {
        var bot = ConGasolineras(Enumerable.Range(1, 20).Select(i => G($"Estacion{i:D2}", 1.5 + i / 100.0, i)).ToArray());

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync(1, TipoCarburante.Gasolina95, default);

        Assert.Contains("16. **Estacion16**", texto);
        Assert.DoesNotContain("1. **Estacion01**", texto);
    }

    /// <summary>El ahorro sobre un depósito ya no se muestra: el listado solo lleva precios.</summary>
    [Fact]
    public async Task NoMuestraElAhorroSobreUnDeposito()
    {
        var bot = ConGasolineras(G("BARATA", 1.700, 0.4), G("CARA", 1.900, 2.1));

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync(0, TipoCarburante.Gasolina95, default);

        // (1,900 - 1,700) * 50 = 10,00 €, que ya no debe aparecer.
        Assert.DoesNotContain("10,00", texto);
        Assert.DoesNotContain("depósito", texto);
    }

    [Fact]
    public async Task EscapaLosRotulosDelMitecoParaNoRomperElMarkdown()
    {
        var bot = ConGasolineras(G("E.S. MAGDAOIL_A", 1.749, 0.4));

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync(0, TipoCarburante.Gasolina95, default);

        Assert.Contains(@"E.S. MAGDAOIL\_A", texto);
    }

    /// <summary>La dirección va bajo el nombre de la gasolinera, sin la distancia.</summary>
    [Fact]
    public async Task MuestraLaDireccionDeCadaGasolinera()
    {
        var bot = ConGasolineras(new Gasolinera("REPSOL", "Avenida de la Industria, 12", 1.749, 0.4));

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync(0, TipoCarburante.Gasolina95, default);

        Assert.Contains("**Gasolineras Alcalá de Guadaíra**", texto);
        Assert.Contains("1. **REPSOL** — **1,749 €/L**", texto);
        Assert.Contains("Avenida de la Industria, 12", texto);
        Assert.DoesNotContain("0,4 km", texto);
        Assert.DoesNotContain("REPSOL — 1,749", texto);
    }

    [Fact]
    public async Task EscapaLaDireccionParaNoRomperElMarkdown()
    {
        var bot = ConGasolineras(new Gasolinera("CEPSA", "Carretera_A-92 km 4", 1.749, 1.2));

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync(0, TipoCarburante.Gasolina95, default);

        Assert.Contains(@"Carretera\_A-92 km 4", texto);
    }

    [Fact]
    public async Task AvisaSiNoHayGasolinerasConPrecioDeGasolina95()
    {
        var bot = ConGasolineras();

        var (texto, teclado) = await bot.Bot.ConstruirTecladoGasolinerasAsync(0, TipoCarburante.Gasolina95, default);

        Assert.Contains("No hay gasolineras con precio de Gasolina 95 E5", texto);
        Assert.Equal(["tipo", "w", "tipo"], teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task PermiteCambiarDeCarburanteSiNoHayResultados()
    {
        var bot = ConGasolineras();

        var (_, teclado) = await bot.Bot.ConstruirTecladoGasolinerasAsync(0, TipoCarburante.Gasolina95, default);

        Assert.Contains("tipo", teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task ConsultaElGasolinaServiceConElRadioDeDiezKilometros()
    {
        var bot = ConGasolineras(G("REPSOL", 1.749, 0.4));

        await bot.Bot.ConstruirTecladoGasolinerasAsync(0, TipoCarburante.Gasolina95, default);

        await bot.Gasolina.Received(1).ObtenerGasolinerasCercaAsync("41", 37.463, -5.981, 10, TipoCarburante.Gasolina95, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OfreceNavegacionSiHayMasDeUnaPaginaDeGasolineras()
    {
        var bot = ConGasolineras(Enumerable.Range(1, 20).Select(i => G($"Estacion{i:D2}", 1.5 + i / 100.0, i)).ToArray());

        var (texto, teclado) = await bot.Bot.ConstruirTecladoGasolinerasAsync(0, TipoCarburante.Gasolina95, default);

        Assert.Contains("Página 1 de 2", texto);

        // En la primera página la flecha apunta a la segunda: gl|codigo|pagina|token.
        Assert.Contains("gl|41004|1|95", teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task EnLaUltimaPaginaLaFlechaApuntaALaAnterior()
    {
        var bot = ConGasolineras(Enumerable.Range(1, 20).Select(i => G($"Estacion{i:D2}", 1.5 + i / 100.0, i)).ToArray());

        var (_, teclado) = await bot.Bot.ConstruirTecladoGasolinerasAsync(1, TipoCarburante.Gasolina95, default);

        Assert.Contains("gl|41004|0|95", teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task AjustaLaPaginaFueraDeRangoAlNumeroDePaginasReal()
    {
        var bot = ConGasolineras(Enumerable.Range(1, 20).Select(i => G($"Estacion{i:D2}", 1.5 + i / 100.0, i)).ToArray());

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync(99, TipoCarburante.Gasolina95, default);

        Assert.Contains("16. **Estacion16**", texto);
        Assert.DoesNotContain("Página 100", texto);
    }
}
