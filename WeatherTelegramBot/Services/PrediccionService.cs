using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using WeatherTelegramBot.Models;

namespace WeatherTelegramBot.Services
{
    /// <summary>
    /// Normaliza el feed de el-tiempo.net a los modelos de <see cref="PrediccionMunicipio"/>.
    /// La API es muy irregular: el mismo campo aparece como array de 15, 24 o 7 entradas, o
    /// como escalar, y <c>proximos_dias</c> repite mañana como primer elemento. Toda esa
    /// variabilidad se resuelve aquí para que el bot solo pinte listas ya limpias.
    /// </summary>
    public class PrediccionService : IPrediccionService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PrediccionService> _logger;

        public PrediccionService(IHttpClientFactory httpClientFactory, ILogger<PrediccionService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<PrediccionMunicipio?> ObtenerPrediccionAsync(
            string codProvincia,
            string codMunicipio,
            CancellationToken cancellationToken = default)
        {
            string url = $"{ElTiempoApi.BaseUrl}/provincias/{codProvincia}/municipios/{codMunicipio}";

            try
            {
                var httpClient = _httpClientFactory.CreateClient(ElTiempoApi.NombreCliente);
                var respuesta = await httpClient.GetFromJsonAsync<JsonElement>(url, cancellationToken);

                return Construir(respuesta);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error de red al consultar la predicción de {Provincia}/{Municipio}", codProvincia, codMunicipio);
                return null;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error al deserializar la predicción de {Provincia}/{Municipio}", codProvincia, codMunicipio);
                return null;
            }
        }

        internal static PrediccionMunicipio Construir(JsonElement raiz)
        {
            string nombre = LeerTexto(raiz, "municipio", "NOMBRE") ?? "tu municipio";
            string elaborado = LeerTexto(raiz, "elaborado") ?? "";

            var pronostico = LeerObjeto(raiz, "pronostico");
            var hoy = pronostico.HasValue ? LeerObjeto(pronostico.Value, "hoy") : null;

            var horarios = ConstruirHorarios(hoy);
            var dias = ConstruirDias(raiz, nombre, hoy);

            return new PrediccionMunicipio(nombre, elaborado, horarios, dias);
        }

        /// <summary>
        /// El bloque "hoy" viene por horas o por tramos de 3 horas según el día. Cada campo
        /// tiene su propia longitud, así que se leen por índice y los que faltan se saltan
        /// en lugar de romper el índice de los demás.
        /// </summary>
        private static List<TramoHorario> ConstruirHorarios(JsonElement? dia)
        {
            var tramos = new List<TramoHorario>();
            if (dia is not { } d)
                return tramos;

            var temperaturas = LeerLista(d, "temperatura");
            var sensaciones = LeerLista(d, "sens_termica");
            var cielos = LeerLista(d, "estado_cielo_descripcion");
            var probabilidades = LeerLista(d, "prob_precipitacion");
            var vientos = LeerLista(d, "viento");

            for (int i = 0; i < temperaturas.Count; i++)
            {
                var viento = i < vientos.Count ? vientos[i] : default;
                tramos.Add(new TramoHorario(
                    Hora: ExtraerHora(viento),
                    Temperatura: LeerNumero(temperaturas[i]),
                    Sensacion: i < sensaciones.Count ? LeerNumero(sensaciones[i]) : null,
                    Cielo: i < cielos.Count ? Texto(cielos[i]) ?? "" : "",
                    ProbPrecipitacion: ProbabilidadDelDia(probabilidades),
                    VientoDireccion: Texto(Propiedad(viento, "direccion")),
                    VientoVelocidad: LeerNumero(Propiedad(viento, "velocidad"))));
            }

            return tramos;
        }

        /// <summary>
        /// El día en curso se toma del bloque "hoy" y el resto de <c>proximos_dias</c>, que
        /// empieza en mañana. El bloque "manana" no se añade porque solo aporta el detalle
        /// por horas y ese día ya aparece resumido en la lista.
        /// </summary>
        private static List<DiaPrediccion> ConstruirDias(
            JsonElement raiz,
            string nombre,
            JsonElement? hoy)
        {
            var dias = new List<DiaPrediccion>();

            if (hoy is { } h && FechaDelDia(h) is { } fechaHoy)
            {
                dias.Add(new DiaPrediccion(
                    nombre,
                    fechaHoy,
                    Cielo: Texto(Primera(LeerLista(h, "estado_cielo_descripcion"))) ?? "",
                    Maxima: Maximo(LeerLista(h, "temperatura")),
                    Minima: Minimo(LeerLista(h, "temperatura")),
                    ProbPrecipitacion: ProbabilidadDelDia(LeerLista(h, "prob_precipitacion")),
                    Viento: MaximoDelViento(LeerLista(h, "viento")),
                    RachaMax: Maximo(LeerLista(h, "racha_max")),
                    UvMax: null));
            }

            var proximos = LeerLista(raiz, "proximos_dias");
            for (int i = 0; i < proximos.Count; i++)
            {
                var dia = proximos[i];
                if (dia.ValueKind != JsonValueKind.Object)
                    continue;

                // Sin fecha no se puede ordenar ni mostrar el día, y el feed incluye entradas vacías
                // al final cuando el horizonte aún no está completo.
                if (FechaDelDia(dia) is not { } fechaFinal)
                    continue;

                if (dias.Count > 0 && fechaFinal <= dias[^1].Fecha)
                    continue;

                dias.Add(new DiaPrediccion(
                    nombre,
                    fechaFinal,
                    Cielo: CieloDelDia(dia),
                    Maxima: TemperaturaDelDia(dia, "max"),
                    Minima: TemperaturaDelDia(dia, "min"),
                    ProbPrecipitacion: ProbabilidadDelDia(LeerLista(dia, "prob_precipitacion")),
                    Viento: VientoDelDia(dia),
                    RachaMax: Maximo(LeerLista(dia, "racha_max")),
                    UvMax: LeerEntero(LeerTexto(dia, "uv_max"))));
            }

            return dias;
        }

        /// <summary>
        /// El MITECO expresa el cielo como código numérico ("11n", "43") cuando no incluye la
        /// descripción. Estos son los códigos más habituales, traducidos al español.
        /// </summary>
        internal static string CieloDesdeCodigo(string codigo) => codigo.TrimEnd('n') switch
        {
            "11" => "Despejado",
            "12" => "Poco nuboso",
            "13" => "Intervalos nubosos",
            "14" => "Nuboso",
            "15" => "Muy nuboso",
            "16" => "Cubierto",
            "17" => "Niebla",
            "43" => "Intervalos nubosos con lluvia escasa",
            "44" => "Nuboso con lluvia escasa",
            "45" => "Muy nuboso con lluvia escasa",
            "46" => "Cubierto con lluvia escasa",
            _ => "Variable"
        };

        /// <summary>
        /// La probabilidad llega por tramos ("00-24", "00-12", "12-24"...) o como escalar.
        /// Se toma el mayor de todos: es el dato que más sense al usuario y evita depender
        /// de en qué posición venga el día completo.
        /// </summary>
        internal static double? ProbabilidadDelDia(List<JsonElement> valores) => Maximo(valores);

        private static string CieloDelDia(JsonElement dia)
        {
            // La descripción es lo ideal, pero cuando falta el MITECO deja el código numérico
            // ("11n", "43") en su lugar. Se detecta porque son solo dígitos y una "n".
            var descripciones = LeerLista(dia, "estado_cielo_descripcion");
            var codigos = LeerLista(dia, "estado_cielo");

            string? codigo = Texto(Primera(codigos)) ?? Texto(Primera(descripciones));

            string? descripcion = descripciones.Count > 0 ? Texto(descripciones[0]) : null;
            if (!string.IsNullOrWhiteSpace(descripcion) && !EsCodigoDeCielo(descripcion))
                return descripcion.Trim();

            return codigo is not null ? CieloDesdeCodigo(codigo) : "";
        }

        private static bool EsCodigoDeCielo(string valor)
        {
            string codigo = valor.TrimEnd('n');
            return codigo.Length > 0 && codigo.All(char.IsAsciiDigit);
        }

        /// <summary>
        /// La temperatura del día llega como objeto {"maxima", "minima"} en el feed real, y
        /// algún municipio la publica abreviada como {"max", "min"}. Cuando es un escalar se
        /// interpreta como la máxima, y si llega suelta en array se saca el extremo.
        /// </summary>
        private static double? TemperaturaDelDia(JsonElement dia, string campo)
        {
            if (dia.TryGetProperty("temperatura", out var temperatura))
            {
                if (temperatura.ValueKind == JsonValueKind.Object)
                {
                    string nombre = campo == "max" ? "maxima" : "minima";
                    return LeerNumero(Propiedad(temperatura, nombre)) ?? LeerNumero(Propiedad(temperatura, campo));
                }

                if (temperatura.ValueKind != JsonValueKind.Array)
                    return campo == "max" ? LeerNumero(temperatura) : null;
            }

            var lista = LeerLista(dia, "temperatura");
            return campo switch
            {
                "max" => Maximo(lista),
                "min" => Minimo(lista),
                _ => null
            };
        }

        private static double? VientoDelDia(JsonElement dia)
        {
            if (dia.TryGetProperty("viento", out var viento))
            {
                // Los días cercanos traen un array de objetos {direccion, velocidad}; los
                // lejanos, un único objeto. Maximo se salta los que no son números, así que
                // con el array devuelve directamente la racha más alta del día.
                if (viento.ValueKind == JsonValueKind.Object)
                    return LeerNumero(Propiedad(viento, "velocidad"));
                if (viento.ValueKind != JsonValueKind.Array)
                    return LeerNumero(viento);
            }

            return MaximoDelViento(LeerLista(dia, "viento"));
        }

        /// <summary>
        /// La velocidad del viento va anidada en <c>velocidad</c> dentro de cada tramo, así que
        /// no se puede usar <see cref="Maximo"/> directamente sobre la lista.
        /// </summary>
        private static double? MaximoDelViento(List<JsonElement> tramos)
        {
            double? maximo = null;
            foreach (var tramo in tramos)
            {
                if (LeerNumero(Propiedad(tramo, "velocidad")) is not { } numero)
                    continue;
                maximo = maximo is null ? numero : Math.Max(maximo.Value, numero);
            }
            return maximo;
        }

        private static string? ExtraerHora(JsonElement viento)
        {
            string? periodo = Texto(Propiedad(Propiedad(viento, "@attributes"), "periodo"));
            return periodo is { Length: >= 2 } ? periodo[..2] : null;
        }

        private static JsonElement Primera(List<JsonElement> valores) =>
            valores.Count > 0 ? valores[0] : default;

        private static double? Maximo(List<JsonElement> valores)
        {
            double? maximo = null;
            foreach (var valor in valores)
            {
                if (LeerNumero(valor) is not { } numero)
                    continue;
                maximo = maximo is null ? numero : Math.Max(maximo.Value, numero);
            }
            return maximo;
        }

        private static double? Minimo(List<JsonElement> valores)
        {
            double? minimo = null;
            foreach (var valor in valores)
            {
                if (LeerNumero(valor) is not { } numero)
                    continue;
                minimo = minimo is null ? numero : Math.Min(minimo.Value, numero);
            }
            return minimo;
        }

        private static JsonElement? LeerObjeto(JsonElement padre, string nombre) =>
            padre.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.Object ? valor : null;

        /// <summary>
        /// Lee un campo venga como array o como escalar, que es la ambigüedad principal del
        /// feed: los primeros días de <c>proximos_dias</c> son arrays y los últimos escalares.
        /// </summary>
        private static List<JsonElement> LeerLista(JsonElement padre, string nombre)
        {
            if (!padre.TryGetProperty(nombre, out var valor) || valor.ValueKind == JsonValueKind.Null)
                return [];

            return valor.ValueKind == JsonValueKind.Array ? valor.EnumerateArray().ToList() : [valor];
        }

        private static string? LeerTexto(JsonElement padre, params string[] ruta)
        {
            var actual = padre;
            foreach (var nombre in ruta)
            {
                if (!actual.TryGetProperty(nombre, out var valor))
                    return null;
                actual = valor;
            }
            return Texto(actual);
        }

        private static string? Texto(JsonElement? valor) => valor is not { } elemento ? null : elemento.ValueKind switch
        {
            JsonValueKind.String => elemento.GetString(),
            JsonValueKind.Number => elemento.ToString(),
            _ => null
        };

        private static JsonElement? Propiedad(JsonElement? valor, string nombre) =>
            valor is { } elemento
            && elemento.ValueKind == JsonValueKind.Object
            && elemento.TryGetProperty(nombre, out var encontrada)
                ? encontrada
                : null;

        private static double? LeerNumero(JsonElement? valor)
        {
            if (valor is not { } elemento)
                return null;

            return elemento.ValueKind switch
            {
                JsonValueKind.String when double.TryParse(elemento.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var numero) => numero,
                JsonValueKind.Number when elemento.TryGetDouble(out var numero) => numero,
                _ => null
            };
        }

        private static int? LeerEntero(string? valor) =>
            valor is not null && int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero) ? numero : null;

        private static DateOnly? LeerFecha(string? valor) =>
            DateOnly.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha) ? fecha : null;

        /// <summary>
        /// El feed real no pone la fecha en la raíz del día, sino en "@attributes". La raíz se
        /// consulta como respaldo por si algún municipio la publica allí.
        /// </summary>
        private static DateOnly? FechaDelDia(JsonElement dia)
        {
            if (Propiedad(dia, "@attributes") is { } atributos)
            {
                if (LeerFecha(LeerTexto(atributos, "fecha")) is { } fecha)
                    return fecha;
            }

            return LeerFecha(LeerTexto(dia, "fecha"));
        }
    }
}