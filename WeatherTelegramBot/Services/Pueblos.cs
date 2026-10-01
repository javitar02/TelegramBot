namespace WeatherTelegramBot.Services
{
    /// <summary>
    /// Un pueblo al que el bot da servicio. Lleva el nombre porque la cabecera de los listados
    /// lo anuncia, y las coordenadas porque las gasolineras se buscan por distancia.
    /// </summary>
    internal readonly record struct Pueblo(
        string Nombre,
        string CodProvincia,
        string CodIne,
        double Latitud,
        double Longitud);

    /// <summary>
    /// El bot da servicio en Alcalá, pero por lo pedido, la mitad de las veces que se consulta
    /// el clima o los precios de la gasolina el parte sale de otro pueblo. Guarromán es el
    /// clásico del chiste; para sumar más basta con meter su INE en la lista.
    /// </summary>
    internal static class Pueblos
    {
        public static readonly Pueblo Alcala = new(
            MunicipioAlcala.Nombre,
            MunicipioAlcala.CodigoProvincia,
            MunicipioAlcala.CodigoIne,
            MunicipioAlcala.Latitud,
            MunicipioAlcala.Longitud);

        /// <summary>
        /// El INE abreviado a cinco dígitos es el que espera la URL de el-tiempo.net. Las
        /// coordenadas son el centroide del término municipal que publica esa misma API, el
        /// mismo criterio con el que se calcularon las de Alcalá.
        /// </summary>
        private static readonly Pueblo[] Alternos =
        [
            new("Guarromán", "23", "23039", 38.18148567, -3.68678524), // Jaén
        ];

        public static readonly IReadOnlyList<Pueblo> Todos = [Alcala, .. Alternos];

        /// <summary>
        /// Tirada del dado, mitad y mitad. Se usa Next(2) en lugar de NextDouble para no
        /// depender de la precisión del flotante en el borde del 50%.
        /// </summary>
        public static bool Toca() => Random.Shared.Next(2) == 0;

        /// <summary>El pueblo de esta tirada: un alterno la mitad de las veces.</summary>
        public static Pueblo Elegir() => Toca() ? Alternos[Random.Shared.Next(Alternos.Length)] : Alcala;

        /// <summary>
        /// El pueblo al que pertenece un INE. Sirve para que la página 2 no vuelva a tirar el
        /// dado: si el listado salió de Guarromán, la página 2 tiene que seguir siendo de
        /// Guarromán. Un INE desconocido cae en Alcalá.
        /// </summary>
        public static Pueblo PorIne(string codIne) =>
            Todos.FirstOrDefault(p => p.CodIne == codIne) is { } encontrado && encontrado != default
                ? encontrado
                : Alcala;
    }
}
