using NSubstitute;
using Telegram.Bot.Types.Enums;
using WeatherTelegramBot.Models;
using WeatherTelegramBot.Services;
using WeatherTelegramBot.Tests.Fakes;

namespace WeatherTelegramBot.Tests.Bot;

public class TecladoMunicipiosTests
{
    private static BotDePrueba ConMunicipios(params string[] nombres)
    {
        var bot = new BotDePrueba();
        var municipios = nombres
            .Select((n, i) => RespuestasJson.Municipio(codigoIne: $"41{i:D4}0001", nombre: n))
            .ToArray();

        bot.Municipios.ObtenerMunicipiosAsync(Arg.Any<CancellationToken>()).Returns(municipios);
        return bot;
    }

    [Fact]
    public async Task MuestraElTituloDelClimaConElNumeroDePaginas()
    {
        var bot = ConMunicipios("Sevilla", "Écija");

        var (texto, _) = await bot.Bot.ConstruirTecladoMunicipiosAsync(0, false, default);

        Assert.Contains("**Municipios de Sevilla**", texto);
        Assert.Contains("página 1 de 1", texto);
        Assert.Contains("Elige el municipio", texto);
    }

    [Fact]
    public async Task ElModoGasofaUsaPrefijosDeCallbackPropios()
    {
        var bot = ConMunicipios("Sevilla", "Écija");

        var (_, teclado) = await bot.Bot.ConstruirTecladoMunicipiosAsync(0, true, default);

        Assert.Contains("g|41000", teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
        Assert.DoesNotContain("w|41000", teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task TruncaElCodigoIneATeCincoDigitos()
    {
        var bot = ConMunicipios("Sevilla");

        var (_, teclado) = await bot.Bot.ConstruirTecladoMunicipiosAsync(0, false, default);

        Assert.Contains("w|41000", teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task DistributeLosMunicipiosEnFilasDeDos()
    {
        var bot = ConMunicipios("Uno", "Dos", "Tres");

        var (_, teclado) = await bot.Bot.ConstruirTecladoMunicipiosAsync(0, false, default);

        var filas = teclado.InlineKeyboard.ToArray();
        Assert.Equal(2, filas[0].Count());
        Assert.Single(filas[1]);
    }

    [Fact]
    public async Task LimitaCadaPaginaASeinteMunicipios()
    {
        var bot = ConMunicipios(Enumerable.Range(1, 45).Select(i => $"Municipio{i:D2}").ToArray());

        var (texto, teclado) = await bot.Bot.ConstruirTecladoMunicipiosAsync(0, false, default);

        Assert.Contains("página 1 de 3", texto);

        var botonesDeMunicipio = teclado.InlineKeyboard
            .TakeWhile(f => f.Any(b => b.CallbackData?.StartsWith("w|") == true))
            .SelectMany(f => f)
            .Count();
        Assert.Equal(20, botonesDeMunicipio);
    }

    [Fact]
    public async Task AjustaUnaPaginaFueraDeRangoAlCatalogo()
    {
        var bot = ConMunicipios("Sevilla");

        var (texto, _) = await bot.Bot.ConstruirTecladoMunicipiosAsync(99, false, default);

        Assert.Contains("página 1 de 1", texto);
    }

    [Fact]
    public async Task AvisaSiElCatalogoEstaVacio()
    {
        var bot = new BotDePrueba();
        bot.Municipios.ObtenerMunicipiosAsync(Arg.Any<CancellationToken>()).Returns([]);

        var (texto, teclado) = await bot.Bot.ConstruirTecladoMunicipiosAsync(0, false, default);

        Assert.Contains("No se pudo cargar el catálogo", texto);
        Assert.Equal(["menu", "ir_clima", "ir_gasofa"], teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task LaUltimaFilaSonLosBotonesDeClimaOGasofa()
    {
        var bot = ConMunicipios("Sevilla", "Écija");

        var (_, teclado) = await bot.Bot.ConstruirTecladoMunicipiosAsync(0, false, default);

        var ultima = teclado.InlineKeyboard.ToArray()[^1];
        Assert.Equal(["ir_clima", "ir_gasofa"], ultima.Select(b => b.CallbackData));
    }
}

public class TextoClimaTests
{
    [Fact]
    public async Task ResumeLaPrediccionConTodosLosCampos()
    {
        var bot = new BotDePrueba();
        bot.Clima.ObtenerTiempoPorMunicipioAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RespuestasJson.Tiempo(
                nombre: "Écija",
                nombreProvincia: "Sevilla",
                descripcion: "Despejado",
                temperaturaActual: "28",
                maxima: "31",
                minima: "19",
                humedad: "35",
                viento: "12",
                precipitacion: "0",
                elaborado: "12/09/2025 10:00"));

        var texto = await bot.Bot.ObtenerTiempoPorCodigoAsync("41054", default);

        Assert.Contains("**El tiempo en Écija** (Sevilla)", texto);
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

        var texto = await bot.Bot.ObtenerTiempoPorCodigoAsync("41091", default);

        Assert.Contains("No se pudo obtener la información", texto);
    }

    [Fact]
    public async Task ConsultaSiempreLaProvinciaDeSevilla()
    {
        var bot = new BotDePrueba();

        await bot.Bot.ObtenerTiempoPorCodigoAsync("41091", default);

        await bot.Clima.Received(1).ObtenerTiempoPorMunicipioAsync("41", "41091", Arg.Any<CancellationToken>());
    }
}

public class TecladoGasolinerasTests
{
    private static BotDePrueba ConGasolineras(params Gasolinera[] gasolineras)
    {
        var bot = new BotDePrueba();
        bot.Gasolina
            .ObtenerGasolinerasCercaAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoGasolineras(gasolineras, "12/09/2025 08:00"));
        return bot;
    }

    private static Gasolinera G(string nombre, double precio, double distancia) =>
        new(nombre, "Calle Real, 1", "Sevilla", 37.388, -5.982, distancia, precio, null, null, null);

    [Fact]
    public async Task MuestraCabeceraConElRadioYElTotalDeGasolineras()
    {
        var bot = ConGasolineras(G("REPSOL", 1.749, 0.4), G("CEPSA", 1.899, 2.1));

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync("41091", 0, default);

        Assert.Contains("**Gasolineras cerca de Sevilla**", texto);
        Assert.Contains("Gasolina 95 E5 · radio 10 km · 2 gasolineras", texto);
    }

    [Fact]
    public async Task NumeraLasGasolinerasDesdeLaPosicionRealDeLaPagina()
    {
        var bot = ConGasolineras(Enumerable.Range(1, 20).Select(i => G($"Estacion{i:D2}", 1.5 + i / 100.0, i)).ToArray());

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync("41091", 1, default);

        Assert.Contains("16. Estacion16", texto);
        Assert.DoesNotContain("1. Estacion01", texto);
    }

    [Fact]
    public async Task CalculaElAhorroSobreUnDepositoDeCincuentaLitros()
    {
        var bot = ConGasolineras(G("BARATA", 1.700, 0.4), G("CARA", 1.900, 2.1));

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync("41091", 0, default);

        // (1,900 - 1,700) * 50 = 10,00 €
        Assert.Contains("hasta **10,00 €**", texto);
    }

    [Fact]
    public async Task EscapaLosRotulosDelMitecoParaNoRomperElMarkdown()
    {
        var bot = ConGasolineras(G("E.S. MAGDAOIL_A", 1.749, 0.4));

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync("41091", 0, default);

        Assert.Contains(@"E.S. MAGDAOIL\_A", texto);
    }

    [Fact]
    public async Task AvisaSiNoHayGasolinerasConPrecioDeGasolina95()
    {
        var bot = ConGasolineras();

        var (texto, teclado) = await bot.Bot.ConstruirTecladoGasolinerasAsync("41091", 0, default);

        Assert.Contains("No hay gasolineras", texto);
        Assert.Equal(["gp|0", "ir_clima", "ir_gasofa"], teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task PideOtroMunicipioConGasolinaSiNoHayResultados()
    {
        var bot = ConGasolineras();

        var (_, teclado) = await bot.Bot.ConstruirTecladoGasolinerasAsync("41091", 0, default);

        Assert.Contains("gp|0", teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task AvisaSiElMunicipioNoEstaEnElCatalogo()
    {
        var bot = new BotDePrueba();
        bot.Municipios.ObtenerMunicipiosAsync(Arg.Any<CancellationToken>()).Returns([]);

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync("99999", 0, default);

        Assert.Contains("No se encontró ese municipio", texto);
    }

    [Fact]
    public async Task AvisaSiElMunicipioNoTieneCoordenadas()
    {
        var bot = new BotDePrueba();
        bot.Municipios.ObtenerMunicipiosAsync(Arg.Any<CancellationToken>())
            .Returns([RespuestasJson.Municipio(nombre: "SinCoordenadas", latitud: null, longitud: null)]);

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync("41091", 0, default);

        Assert.Contains("No tengo coordenadas de SinCoordenadas", texto);
    }

    [Fact]
    public async Task ConsultaElGasolinaServiceConElRadioDeDiezKilometros()
    {
        var bot = ConGasolineras(G("REPSOL", 1.749, 0.4));

        await bot.Bot.ConstruirTecladoGasolinerasAsync("41091", 0, default);

        await bot.Gasolina.Received(1).ObtenerGasolinerasCercaAsync("41", 37.388, -5.982, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OfreceNavegacionSiHayMasDeUnaPaginaDeGasolineras()
    {
        var bot = ConGasolineras(Enumerable.Range(1, 20).Select(i => G($"Estacion{i:D2}", 1.5 + i / 100.0, i)).ToArray());

        var (texto, teclado) = await bot.Bot.ConstruirTecladoGasolinerasAsync("41091", 0, default);

        Assert.Contains("Página 1 de 2", texto);

        // En la primera página la flecha apunta a la segunda: gl|codigo|1.
        Assert.Contains("gl|41091|1", teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task EnLaUltimaPaginaLaFlechaApuntaALaAnterior()
    {
        var bot = ConGasolineras(Enumerable.Range(1, 20).Select(i => G($"Estacion{i:D2}", 1.5 + i / 100.0, i)).ToArray());

        var (_, teclado) = await bot.Bot.ConstruirTecladoGasolinerasAsync("41091", 1, default);

        Assert.Contains("gl|41091|0", teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public async Task AjustaLaPaginaFueraDeRangoAlNumeroDePaginasReal()
    {
        var bot = ConGasolineras(Enumerable.Range(1, 20).Select(i => G($"Estacion{i:D2}", 1.5 + i / 100.0, i)).ToArray());

        var (texto, _) = await bot.Bot.ConstruirTecladoGasolinerasAsync("41091", 99, default);

        Assert.Contains("16. Estacion16", texto);
        Assert.DoesNotContain("Página 100", texto);
    }
}
