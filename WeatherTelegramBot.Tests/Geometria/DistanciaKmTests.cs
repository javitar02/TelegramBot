using WeatherTelegramBot.Services;
using WeatherTelegramBot.Tests.Fakes;

namespace WeatherTelegramBot.Tests.Geometria;

public class DistanciaKmTests
{
    [Fact]
    public void LaDistanciaEntreUnPuntoYElMismoEsCero()
    {
        Assert.Equal(0, GasolinaService.DistanciaKm(37.388, -5.982, 37.388, -5.982), 9);
    }

    [Fact]
    public void CalculaLaDistanciaDeUnGradoDeLatitud()
    {
        // Un grado de latitud son unos 111 km en cualquier punto del globo.
        double distancia = GasolinaService.DistanciaKm(37.0, 0.0, 38.0, 0.0);

        Assert.InRange(distancia, 110.5, 111.5);
    }

    [Fact]
    public void CalculaLaDistanciaRealEntreDosGasolinerasDeSevilla()
    {
        // Dos puntos separados por poco más de un kilómetro en la misma calle.
        double distancia = GasolinaService.DistanciaKm(37.388, -5.982, 37.397, -5.982);

        Assert.InRange(distancia, 0.9, 1.1);
    }

    [Fact]
    public void EsSimetrica()
    {
        double a = GasolinaService.DistanciaKm(37.388, -5.982, 37.420, -5.900);
        double b = GasolinaService.DistanciaKm(37.420, -5.900, 37.388, -5.982);

        Assert.Equal(a, b, 9);
    }

    [Fact]
    public void CruzaElAtlantico()
    {
        // Sevilla a Nueva York por la superficie del globo, unos 5.735 km.
        double distancia = GasolinaService.DistanciaKm(37.388, -5.982, 40.712, -74.006);

        Assert.InRange(distancia, 5700, 5770);
    }
}

public class TryParseDecimalTests
{
    [Theory]
    [InlineData("1,749", 1.749)]
    [InlineData("1.749", 1.749)]
    [InlineData("0,5", 0.5)]
    [InlineData("  1,749  ", 1.749)]
    [InlineData("37,474333", 37.474333)]
    [InlineData("-5,982", -5.982)]
    [InlineData("1E3", 1000)]
    public void AceptaLosFormatosDelMiteco(string bruto, double esperado)
    {
        Assert.True(GasolinaService.TryParseDecimal(bruto, out double resultado));
        Assert.Equal(esperado, resultado, 9);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N/D")]
    [InlineData("abc")]
    public void RechazaLoQueNoEsUnNumero(string? bruto)
    {
        Assert.False(GasolinaService.TryParseDecimal(bruto, out double resultado));
        Assert.Equal(0, resultado);
    }
}
