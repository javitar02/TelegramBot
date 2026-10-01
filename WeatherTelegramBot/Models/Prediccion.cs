namespace WeatherTelegramBot.Models
{
    /// <summary>
    /// Vista normalizada de la predicción de un municipio. La API mezcla arrays por hora y
    /// escalares según el día; aquí ya viene todo plano y con los números en double.
    /// </summary>
    /// <param name="Nombre">Nombre del municipio.</param>
    /// <param name="Fecha">Fecha en ISO (yyyy-MM-dd) del día al que se refiere la previsión.</param>
    /// <param name="Cielo">Descripción del cielo más representativa del día.</param>
    /// <param name="Maxima">Temperatura máxima en grados.</param>
    /// <param name="Minima">Temperatura mínima en grados.</param>
    /// <param name="ProbPrecipitacion">Probabilidad de lluvia en tanto por ciento.</param>
    /// <param name="Viento">Velocidad del viento en km/h.</param>
    /// <param name="RachaMax">Racha máxima en km/h, si el MITECO la publica.</param>
    /// <param name="UvMax">Índice UV máximo, si el MITECO lo publica.</param>
    public record DiaPrediccion(
        string Nombre,
        DateOnly Fecha,
        string Cielo,
        double? Maxima,
        double? Minima,
        double? ProbPrecipitacion,
        double? Viento,
        double? RachaMax,
        int? UvMax);

    /// <summary>Resumen horario de un día, para la vista de detalle.</summary>
    /// <param name="Hora">Hora local del tramo en formato HH, o null si el feed no la etiqueta.</param>
    /// <param name="Temperatura">Temperatura en grados.</param>
    /// <param name="Sensacion">Sensación térmica en grados.</param>
    /// <param name="Cielo">Descripción del cielo del tramo.</param>
    /// <param name="ProbPrecipitacion">Probabilidad de lluvia en tanto por ciento.</param>
    /// <param name="VientoDireccion">Dirección del viento (N, S, E...).</param>
    /// <param name="VientoVelocidad">Velocidad del viento en km/h.</param>
    public record TramoHorario(
        string? Hora,
        double? Temperatura,
        double? Sensacion,
        string Cielo,
        double? ProbPrecipitacion,
        string? VientoDireccion,
        double? VientoVelocidad);

    /// <summary>
    /// Predicción completa de un municipio: el desglose por horas de un día y el resumen de
    /// los días siguientes.
    /// </summary>
    public record PrediccionMunicipio(
        string Nombre,
        string Elaborado,
        IReadOnlyList<TramoHorario> Horarios,
        IReadOnlyList<DiaPrediccion> Dias);
}