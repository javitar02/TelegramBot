namespace WeatherTelegramBot.Services
{
    /// <summary>
    /// Un pueblo al que el bot da servicio. Lleva el nombre porque la cabecera de los listados
    /// lo anuncia, y las coordenadas porque las gasolineras se buscan por distancia. IdMunicipio
    /// es el código que usa el MITECO en el feed de carburantes (no es el INE) y es lo que
    /// filtra las estaciones del propio término municipal.
    /// </summary>
    internal readonly record struct Pueblo(
        string Nombre,
        string NombreProvincia,
        string CodProvincia,
        string CodIne,
        double Latitud,
        double Longitud,
        string IdMunicipio)
    {
        /// <summary>
        /// El nombre con la provincia detrás, para los encabezados donde el pueblo aparece
        /// solo: sin provincia, Guarromán no dice de dónde es.
        /// </summary>
        public string NombreConProvincia => $"{Nombre} ({NombreProvincia})";
    }

    /// <summary>
    /// Los pueblos por los que rota el bot. El orden del array es el de la rotación: al entrar
    /// en el clima se empieza por el primero y cada pulsación del botón de cambiar pueblo
    /// avanza uno, volviendo a dar la vuelta por el último. Guarromán es el clásico del
    /// chiste; para sumar o quitar uno basta con tocar el array.
    /// </summary>
    internal static class Pueblos
    {
        public static readonly Pueblo Alcala = new(
            MunicipioAlcala.Nombre,
            MunicipioAlcala.NombreProvincia,
            MunicipioAlcala.CodigoProvincia,
            MunicipioAlcala.CodigoIne,
            MunicipioAlcala.Latitud,
            MunicipioAlcala.Longitud,
            MunicipioAlcala.IdMunicipio);

        /// <summary>
        /// La rotación, en el orden en que se van mostrando. El INE abreviado a cinco dígitos
        /// es el que espera la URL de el-tiempo.net. Las coordenadas son el centroide del
        /// término municipal que publica esa misma API, el mismo criterio con el que se
        /// calcularon las de Alcalá. El IdMunicipio es el del feed de carburantes del MITECO.
        /// </summary>
        public static readonly IReadOnlyList<Pueblo> Todos =
        [
            new("Guarromán", "Jaén", "23", "23039", 38.18148567, -3.68678524, "3533"),
            new("Lopera", "Jaén", "23", "23056", 37.94392635, -4.21432714, "3550"),
            Alcala,
        ];

        /// <summary>El pueblo por el que arranca la rotación.</summary>
        public static Pueblo Inicial => Todos[0];

        /// <summary>Posición de un INE en la rotación, o -1 si no está en la lista.</summary>
        private static int IndiceDe(string codIne)
        {
            for (int i = 0; i < Todos.Count; i++)
            {
                if (Todos[i].CodIne == codIne)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// El pueblo al que pertenece un INE. Sirve para que los botones que no cambian de
        /// pueblo (cambiar de carburante, volver al clima) saquen el parte del mismo sitio
        /// que ya está en pantalla. Un INE desconocido cae en el primero de la rotación.
        /// </summary>
        public static Pueblo PorIne(string codIne)
        {
            int indice = IndiceDe(codIne);

            return indice < 0 ? Inicial : Todos[indice];
        }

        /// <summary>
        /// El siguiente de la rotación para el botón de cambiar pueblo, dando la vuelta al
        /// último. Como el bot es stateless no hay contador: el pueblo en el que se está solo
        /// se conoce por el INE que viaja en el callback, así que el siguiente se deduce de ahí.
        /// Un INE desconocido se resuelve en el primero, y por tanto avanza al segundo, para
        /// que el botón nunca se quede en el pueblo que ya se está viendo.
        /// </summary>
        public static Pueblo Siguiente(string codIne)
        {
            int indice = IndiceDe(codIne);

            return Todos[(indice < 0 ? 0 : indice + 1) % Todos.Count];
        }
    }
}
