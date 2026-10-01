using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WeatherTelegramBot.Services;
using WeatherTelegramBot.Tests.Fakes;

namespace WeatherTelegramBot.Tests.Bot;

public class FormatoTextoTests
{
    public static TheoryData<double, int, string> CasosDeFormato
    {
        get
        {
            var datos = new TheoryData<double, int, string>();
            datos.Add(1.749, 3, "1,749");
            datos.Add(1.7, 3, "1,700");
            datos.Add(0.5, 2, "0,50");
            datos.Add(1234.5678, 2, "1234,57");
            datos.Add(10d, 0, "10");
            datos.Add(3.46, 1, "3,5");
            datos.Add(0d, 3, "0,000");
            return datos;
        }
    }

    [Theory]
    [MemberData(nameof(CasosDeFormato))]
    public void FormateaConComaDecimalYSeparadorDeMillares(double valor, int decimales, string esperado)
    {
        Assert.Equal(esperado, TelegramBotService.Formato(valor, decimales));
    }

    [Fact]
    public void ElFormatoFijoNoAgrupaLosMillares()
    {
        // "F2" no lleva separador de millares: sirve para precios, donde 1.234,57 se
        // confundiría con mil euros coma cincuenta y siete.
        Assert.Equal("1234,57", TelegramBotService.Formato(1234.5678, 2));
    }

    [Theory]
    [InlineData("E.S. MAGDAOIL", @"E.S. MAGDAOIL")]
    [InlineData("REPSOL_SUR", @"REPSOL\_SUR")]
    [InlineData("BP*24H", @"BP\*24H")]
    [InlineData("GAS `NOVA`", @"GAS \`NOVA\`")]
    [InlineData("[CENTRO]", @"\[CENTRO]")]
    public void EscapaLosCaracteresDelMarkdownDeTelegram(string entrada, string esperado)
    {
        Assert.Equal(esperado, TelegramBotService.Escapar(entrada));
    }

    [Theory]
    [InlineData("Sevilla", 'S')]
    [InlineData("  Écija", 'É')]
    [InlineData("el Arahal", 'E')]
    [InlineData("", '#')]
    [InlineData("   ", '#')]
    [InlineData(null, '#')]
    public void ObtieneLaInicialDelNombreEnMayuscula(string? nombre, char esperado)
    {
        Assert.Equal(esperado, TelegramBotService.ObtenerInicial(nombre!));
    }
}

public class FilaNavegacionTests
{
    private static string[] Datos(InlineKeyboardButton[] fila) => [.. fila.Select(b => b.CallbackData!)];

    [Fact]
    public void EnLaPrimeraPaginaNoOfreceAtras()
    {
        var fila = TelegramBotService.ConstruirFilaNavegacion(p => $"pg|{p}", 0, 3);

        Assert.Equal(["noop|1/3", "pg|1"], Datos(fila));
    }

    [Fact]
    public void EnLaUltimaPaginaNoOfreceAdelante()
    {
        var fila = TelegramBotService.ConstruirFilaNavegacion(p => $"pg|{p}", 2, 3);

        Assert.Equal(["pg|1", "noop|3/3"], Datos(fila));
    }

    [Fact]
    public void EnUnaPaginaIntermediaOfreceNavegacionCompleta()
    {
        var fila = TelegramBotService.ConstruirFilaNavegacion(p => $"pg|{p}", 1, 3);

        Assert.Equal(["pg|0", "noop|2/3", "pg|2"], Datos(fila));
    }

    [Fact]
    public void SiSoloHayUnaPaginaMuestraSoloElIndicador()
    {
        var fila = TelegramBotService.ConstruirFilaNavegacion(p => $"pg|{p}", 0, 1);

        Assert.Equal(["noop|1/1"], Datos(fila));
    }

    [Fact]
    public void UsaElPrefijoDeCallbackQueLePasaElLlamante()
    {
        // Tres páginas para que la fila tenga flecha de ida y de vuelta.
        var fila = TelegramBotService.ConstruirFilaNavegacion(p => $"gp|{p}", 1, 3);

        Assert.Equal(["gp|0", "noop|2/3", "gp|2"], Datos(fila));
    }
}

public class TecladoAvisoTests
{
    [Fact]
    public void UsaElBotonDeMenuPorDefecto()
    {
        var teclado = TelegramBotService.TecladoAviso();

        Assert.Equal(["menu", "ir_clima", "ir_gasofa"], teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public void PermitePersonalizarEtiquetaYCallback()
    {
        var teclado = TelegramBotService.TecladoAviso("🔙 Otro municipio", "gp|0");

        var boton = teclado.InlineKeyboard.First().Single();
        Assert.Equal("🔙 Otro municipio", boton.Text);
        Assert.Equal("gp|0", boton.CallbackData);
    }
}

public class BotonesInicialesTests
{
    [Fact]
    public void CreaUnBotonPorInicialSinRepetir()
    {
        var municipios = new[]
        {
            RespuestasJson.Municipio(nombre: "Sevilla"),
            RespuestasJson.Municipio(nombre: "Sevilla"),
            RespuestasJson.Municipio(nombre: "Écija"),
            RespuestasJson.Municipio(nombre: "Estepa")
        };

        var botones = TelegramBotService.ConstruirBotonesIniciales(municipios, "jp").SelectMany(f => f).ToArray();

        // É y E son letras distintas, así que Écija y Estepa reciben botones separados.
        Assert.Equal(["E", "S", "É"], botones.Select(b => b.Text));
        Assert.Equal(["jp|E", "jp|É", "jp|S"], botones.Select(b => b.CallbackData).ToHashSet());
    }

    [Fact]
    public void OrdenaLasInicialesPorCodigoDeCaracter()
    {
        var municipios = new[]
        {
            RespuestasJson.Municipio(nombre: "Sevilla"),
            RespuestasJson.Municipio(nombre: "Aguadulce"),
            RespuestasJson.Municipio(nombre: "Marchena"),
            RespuestasJson.Municipio(nombre: "Écija")
        };

        var botones = TelegramBotService.ConstruirBotonesIniciales(municipios, "jp").SelectMany(f => f).ToArray();

        // Las letras acentuadas caen al final porque se comparan por su valor Unicode,
        // no por su posición en el alfabeto español.
        Assert.Equal(["A", "M", "S", "É"], botones.Select(b => b.Text));
    }

    [Fact]
    public void AgrupaLasInicialesEnFilasDeNueve()
    {
        var municipios = Enumerable.Range(0, 20)
            .Select(i => RespuestasJson.Municipio(nombre: $"{(char)('A' + i)}unicipio"))
            .ToArray();

        var filas = TelegramBotService.ConstruirBotonesIniciales(municipios, "jp").ToArray();

        Assert.Equal(3, filas.Length);
        Assert.Equal(9, filas[0].Length);
        Assert.Equal(9, filas[1].Length);
        Assert.Equal(2, filas[2].Length);
    }

    [Fact]
    public void DevuelveElPrefijoDeSaltoQueLePasaElLlamante()
    {
        var municipios = new[] { RespuestasJson.Municipio(nombre: "Sevilla") };

        var botones = TelegramBotService.ConstruirBotonesIniciales(municipios, "gj").SelectMany(f => f).ToArray();

        Assert.Equal("gj|S", Assert.Single(botones).CallbackData);
    }
}
