namespace WeatherTelegramBot.Services
{
    /// <summary>
    /// El bot da servicio en Alcalá, pero por lo pedido, la mitad de las veces que se consulta
    /// el clima el parte sale de otro pueblo. Guarromán es el clásico del chiste; para sumar
    /// más basta con meter su INE en la lista.
    /// </summary>
    internal static class MunicipiosAlternos
    {
        /// <summary>
        /// Un pueblo con el que el MITECO responde. El código INE va abreviado a cinco
        /// dígitos, que es lo que espera la URL de el-tiempo.net.
        /// </summary>
        internal readonly record struct Alterno(string CodProvincia, string CodIne);

        internal static readonly Alterno[] Todos =
        [
            new("23", "23039"), // Guarromán, en Jaén
        ];

        /// <summary>
        /// Tirada del dado, mitad y mitad. Se usa Next(2) en lugar de NextDouble para no
        /// depender de la precisión del flotante en el borde del 50%.
        /// </summary>
        public static bool Toca() => Random.Shared.Next(2) == 0;

        /// <summary>El pueblo alterno de esta tirada, o null si le toca Alcalá.</summary>
        public static Alterno? Elegir() => Toca() ? Todos[Random.Shared.Next(Todos.Length)] : null;
    }
}
