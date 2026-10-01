using Telegram.Bot.Types.ReplyMarkups;
using WeatherTelegramBot.Services;

namespace WeatherTelegramBot.Tests.Bot;

public class FormatoTextoTests
{
    [Theory]
    [InlineData(1.2345, 1, "1,2")]
    [InlineData(1.749, 3, "1,749")]
    [InlineData(1.749, 0, "2")]
    [InlineData(0.5, 2, "0,50")]
    public void FormateaLosNumerosConComaDecimal(float valor, int decimales, string esperado)
    {
        Assert.Equal(esperado, TelegramBotService.Formato(valor, decimales));
    }

    [Theory]
    [InlineData("E.S. MAGDAOIL_A", @"E.S. MAGDAOIL\_A")]
    [InlineData("A*B", @"A\*B")]
    [InlineData("Back`tick", @"Back\`tick")]
    [InlineData("[enlace]", @"\[enlace]")]
    [InlineData("Sin caracteres", "Sin caracteres")]
    public void EscapaLosCaracteresDelMarkdownDeTelegram(string entrada, string esperado)
    {
        Assert.Equal(esperado, TelegramBotService.Escapar(entrada));
    }

    [Fact]
    public void EscapaVariasVecesElMismoCaracterer()
    {
        Assert.Equal(@"a\_b\_c", TelegramBotService.Escapar("a_b_c"));
    }
}

public class FilaNavegacionTests
{
    private static string[] Datos(InlineKeyboardButton[] fila) =>
        fila.Select(b => b.CallbackData ?? "").ToArray();

    [Fact]
    public void EnLaPrimeraPaginaNoOfreceAtras()
    {
        var fila = TelegramBotService.ConstruirFilaNavegacion(p => $"gl|{p}", 0, 3);

        Assert.Equal(["noop|1/3", "gl|1"], Datos(fila));
    }

    [Fact]
    public void EnLaUltimaPaginaNoOfreceAdelante()
    {
        var fila = TelegramBotService.ConstruirFilaNavegacion(p => $"gl|{p}", 2, 3);

        Assert.Equal(["gl|1", "noop|3/3"], Datos(fila));
    }

    [Fact]
    public void EnUnaPaginaIntermediaOfreceNavegacionCompleta()
    {
        var fila = TelegramBotService.ConstruirFilaNavegacion(p => $"gl|{p}", 1, 3);

        Assert.Equal(["gl|0", "noop|2/3", "gl|2"], Datos(fila));
    }

    [Fact]
    public void SiSoloHayUnaPaginaMuestraSoloElIndicador()
    {
        var fila = TelegramBotService.ConstruirFilaNavegacion(p => $"gl|{p}", 0, 1);

        Assert.Equal(["noop|1/1"], Datos(fila));
    }
}

public class TecladoAvisoTests
{
    [Fact]
    public void UsaElBotonDeMenuPorDefecto()
    {
        var teclado = TelegramBotService.TecladoAviso();

        Assert.Equal(["menu", "w", "tipo"], teclado.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData));
    }

    [Fact]
    public void PermitePersonalizarEtiquetaYCallback()
    {
        var teclado = TelegramBotService.TecladoAviso("🔙 Volver", "menu");

        var boton = teclado.InlineKeyboard.First().Single();
        Assert.Equal("🔙 Volver", boton.Text);
        Assert.Equal("menu", boton.CallbackData);
    }
}
