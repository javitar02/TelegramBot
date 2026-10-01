using System.Globalization;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WeatherTelegramBot.Models;
using WeatherTelegramBot.Responses;

namespace WeatherTelegramBot.Services
{
    public class TelegramBotService : BackgroundService
    {
        private const int GasolinerasPorPagina = 15;

        private const string BotonClima = "☀️ Consultar Clima";
        private const string BotonGasofa = "⛽ Consultar Gasofa";
        private const string BotonMenu = "🏠 Menú principal";
        private const string BotonCambiarCarburante = "🔄 Cambiar carburante";
        private const string BotonPrediccion = "📅 Ver predicción";

        private const string CallbackMenu = "menu";

        /// <summary>Clima de Alcalá, que ya no necesita el código INE en el callback.</summary>
        private const string CallbackClima = "w";

        /// <summary>Pregunta con qué carburante comparar, antes de mostrar las gasolineras.</summary>
        private const string CallbackElegirCarburante = "tipo";

        /// <summary>Gasolineras de Alcalá con el carburante elegido ("ga|95|pt").</summary>
        private const string CallbackGasolineras = "ga";

        private const string CallbackPrediccion = "pr";

        /// <summary>Paginación del listado de gasolineras ("gl|41004|1|95|pt").</summary>
        private const string CallbackGasolinerasPagina = "gl";

        /// <summary>
        /// Alterna pesetas y euros sin salir del listado. Es una acción aparte y no un parámetro
        /// de la paginación porque al pulsarla hay que recargar el precio: el callback lleva la
        /// moneda nueva ya puesta, que es lo mismo que hace "gl" con la página.
        /// </summary>
        private const string CallbackGasolinerasMoneda = "gm";

        /// <summary>
        /// Pasa de un carburante al otro directamente, sin volver a preguntar: el botón sabe
        /// por el callback cuál se está viendo y solo tiene que invertirlo.
        /// </summary>
        private const string CallbackGasolinerasCarburante = "gc";

        // En los custom format de .NET la coma es separador de millares, no decimal:
        // hay que pedir la cultura española y usar F1/F2/F3 para obtener "3,4" y "1,799".
        private static readonly CultureInfo CultureEspanol = CultureInfo.GetCultureInfo("es-ES");

        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<TelegramBotService> _logger;
        private readonly IWeatherService _weatherService;
        private readonly IGasolinaService _gasolinaService;
        private readonly IPrediccionService _prediccionService;

        public TelegramBotService(
            ITelegramBotClient botClient,
            IWeatherService weatherService,
            IGasolinaService gasolinaService,
            IPrediccionService prediccionService,
            ILogger<TelegramBotService> logger)
        {
            _botClient = botClient;
            _weatherService = weatherService;
            _gasolinaService = gasolinaService;
            _prediccionService = prediccionService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            _logger.LogInformation("Iniciando Bot de Telegram...");

            // En Telegram.Bot 22.x StartReceiving devuelve void: el apagado se controla
            // cancelando stoppingToken, que es lo que interrumpe el Task.Delay de abajo.
            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandlePollingErrorAsync,
                receiverOptions,
                cancellationToken: stoppingToken
            );

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Deteniendo Bot de Telegram...");
            }
        }

        /// <summary>
        /// Bienvenida: el bot solo cubre Alcalá de Guadaíra, así que el menú ofrece
        /// directamente el clima de la ciudad y los precios de carburante.
        /// </summary>
        private static string Bienvenida =>
            "¡Hola! Soy tu bot del tiempo y experto en gasofa 👋\n\n" +
            $"Doy servicio en {MunicipioAlcala.Nombre}: elige el clima o los precios de la gasolina ⛽";

        internal async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.CallbackQuery is not null)
                {
                    await HandleCallbackQueryAsync(update.CallbackQuery, cancellationToken);
                    return;
                }

                if (update.Message is not { Text: { } messageText } message)
                    return;

                long chatId = message.Chat.Id;
                string texto = messageText.Trim();
                _logger.LogInformation("Mensaje recibido en chat {ChatId}: {Text}", chatId, texto);

                if (texto.Equals("/start", StringComparison.OrdinalIgnoreCase))
                {
                    await EnviarMenu(botClient, chatId, cancellationToken);
                    return;
                }

                await botClient.SendMessage(
                    chatId: chatId,
                    text: "No te he entendido. Usa los botones del menú para consultar el tiempo 🌤️ o los precios de la gasolina ⛽",
                    cancellationToken: cancellationToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar la actualización {UpdateId}", update.Id);
            }
        }

        private async Task EnviarMenu(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            await botClient.SendMessage(
                chatId: chatId,
                text: Bienvenida,
                parseMode: ParseMode.Markdown,
                replyMarkup: TecladoInicio(),
                cancellationToken: cancellationToken
            );
        }

        internal async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            if (callbackQuery.Message is null || callbackQuery.Data is null)
                return;

            long chatId = callbackQuery.Message.Chat.Id;
            int messageId = callbackQuery.Message.MessageId;
            string[] partes = callbackQuery.Data.Split('|');
            string accion = partes[0];
            string Parte(int indice) => partes.Length > indice ? partes[indice] : "";

            switch (accion)
            {
                case "noop":
                    await _botClient.AnswerCallbackQuery(callbackQuery.Id, text: $"Ya estás en la página {Parte(1)}.", cancellationToken: cancellationToken);
                    return;

                case CallbackMenu:
                    await ReemplazarMensaje(chatId, messageId, Bienvenida, TecladoInicio(), cancellationToken);
                    break;

                case CallbackClima:
                    await ReemplazarMensaje(chatId, messageId, await ObtenerTiempoAsync(cancellationToken), ConstruirTecladoClima(), cancellationToken);
                    break;

                case CallbackPrediccion:
                    await ReemplazarMensaje(
                        chatId,
                        messageId,
                        await ObtenerPrediccionAsync(cancellationToken),
                        TecladoAviso("🗺️ Volver al clima", CallbackClima),
                        cancellationToken);
                    break;

                case CallbackGasolineras:
                case CallbackGasolinerasPagina:
                case CallbackGasolinerasMoneda:
                case CallbackGasolinerasCarburante:
                    // El bot es stateless, así que carburante y moneda viajan en el propio callback:
                    // "ga|95|pt" arranca el listado, "gl|41004|1|95|pt" lo pagina, "gm|41004|1|95|pt"
                    // cambia la moneda y "gc|41004|1|95|pt" cambia el carburante. Las tres últimas
                    // dejan el resto del listado intacto.
                    // "ga" es la única acción corta: no lleva INE ni página, así que sus campos
                    // van desplazados una posición respecto a las otras.
                    bool esContinuacion = accion != CallbackGasolineras;
                    var tipoElegido = TipoCarburanteExtensions.DesdeToken(Parte(esContinuacion ? 3 : 1));
                    if (tipoElegido is not { } tipo)
                    {
                        var monedaFallida = MonedaExtensions.DesdeToken(Parte(esContinuacion ? 4 : 2)) ?? Moneda.Euros;
                        await ReemplazarMensaje(chatId, messageId, PreguntaCarburante, TecladoCarburante(monedaFallida), cancellationToken);
                        break;
                    }

                    // Al arrancar se tira el dado, pero al paginar manda el INE del callback:
                    // si no, la página 2 podría salir de un pueblo distinto al de la página 1.
                    var pueblo = esContinuacion ? Pueblos.PorIne(Parte(1)) : Pueblos.Elegir();

                    int pagina = esContinuacion && int.TryParse(Parte(2), out int gl) ? gl : 0;

                    // Si no se reconoce el token (o no viene) se cae en euros, que es como
                    // arranca siempre el listado; el botón es lo único que lleva a pesetas.
                    var moneda = MonedaExtensions.DesdeToken(Parte(esContinuacion ? 4 : 2)) ?? Moneda.Euros;
                    if (accion == CallbackGasolinerasMoneda)
                        moneda = moneda.Contraria();

                    // El botón de carburante no pregunta: directamente invierte el que hay
                    // en pantalla, así que el listado sale ya con el otro combustible.
                    if (accion == CallbackGasolinerasCarburante)
                        tipo = tipo.Contrario();

                    var gasolineras = await ConstruirTecladoGasolinerasAsync(pueblo, pagina, tipo, moneda, cancellationToken);
                    await ReemplazarMensaje(chatId, messageId, gasolineras.Texto, gasolineras.Teclado, cancellationToken);
                    break;

                case CallbackElegirCarburante:
                    // El botón del menú va sin moneda, así que Parte(1) vacío cae en euros:
                    // al entrar por el menú se empieza siempre en euros.
                    await ReemplazarMensaje(
                        chatId,
                        messageId,
                        PreguntaCarburante,
                        TecladoCarburante(MonedaExtensions.DesdeToken(Parte(1)) ?? Moneda.Euros),
                        cancellationToken);
                    break;
            }

            await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
        }

        private async Task ReemplazarMensaje(long chatId, int messageId, string texto, InlineKeyboardMarkup teclado, CancellationToken cancellationToken)
        {
            try
            {
                await _botClient.EditMessageText(
                    chatId: chatId,
                    messageId: messageId,
                    text: texto,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: teclado,
                    cancellationToken: cancellationToken
                );
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
            {
                // Telegram responde 400 si el teclado y el texto no han cambiado: es inocuo.
            }
        }

        /// <summary>Teclado del clima de Alcalá: predicción y vuelta al menú.</summary>
        internal static InlineKeyboardMarkup ConstruirTecladoClima() =>
            TecladoAviso(BotonPrediccion, CallbackPrediccion);

        /// <summary>
        /// Fila de acceso directo a los dos menús. Se añade al pie de todos los teclados
        /// para que se pueda cambiar de consulta sin volver al menú principal, y sustituye
        /// el reply keyboard: los botones inline no desaparecen al enviar otro mensaje.
        /// </summary>
        private static InlineKeyboardButton[] ConstruirFilaPrincipal() =>
        [
            new(BotonClima) { CallbackData = CallbackClima },
            new(BotonGasofa) { CallbackData = CallbackElegirCarburante }
        ];

        internal static InlineKeyboardMarkup TecladoAviso(
            string etiquetaBoton = BotonMenu,
            string callbackData = CallbackMenu)
        {
            return new InlineKeyboardMarkup(
            [
                [new InlineKeyboardButton(etiquetaBoton) { CallbackData = callbackData }],
                ConstruirFilaPrincipal()
            ]);
        }

        internal static InlineKeyboardButton[] ConstruirFilaNavegacion(
            Func<int, string> callbackParaPagina,
            int pagina,
            int totalPaginas)
        {
            var navegacion = new List<InlineKeyboardButton>();

            if (pagina > 0)
                navegacion.Add(new InlineKeyboardButton("◀️ A") { CallbackData = callbackParaPagina(pagina - 1) });

            navegacion.Add(new InlineKeyboardButton($"{pagina + 1}/{totalPaginas}")
            {
                CallbackData = $"noop|{pagina + 1}/{totalPaginas}"
            });

            if (pagina < totalPaginas - 1)
                navegacion.Add(new InlineKeyboardButton("B ▶️") { CallbackData = callbackParaPagina(pagina + 1) });

            return [.. navegacion];
        }

        /// <summary>
        /// Parte del clima. La mitad de las veces el dado saca un pueblo alterno, así que el
        /// municipio se decide aquí y el render se delega en la sobrecarga de abajo.
        /// </summary>
        internal Task<string> ObtenerTiempoAsync(CancellationToken cancellationToken)
        {
            var pueblo = Pueblos.Elegir();

            return ObtenerTiempoAsync(pueblo.CodProvincia, pueblo.CodIne, cancellationToken);
        }

        internal async Task<string> ObtenerTiempoAsync(
            string codProvincia,
            string codIne,
            CancellationToken cancellationToken)
        {
            var clima = await _weatherService.ObtenerTiempoPorMunicipioAsync(codProvincia, codIne, cancellationToken);

            if (clima?.Municipio is null || clima.Temperaturas is null || clima.EstadoCielo is null)
                return "⚠️ No se pudo obtener la información del clima en este momento.";

            return $"{EmojiCielo(clima.EstadoCielo.Descripcion)} **El tiempo en {clima.Municipio.Nombre}** ({clima.Municipio.NombreProvincia})\n\n" +
                   $"• Estado: {clima.EstadoCielo.Descripcion}\n" +
                   $"• Actual: {clima.TemperaturaActual}°C | Mín: {clima.Temperaturas.Minima}°C | Máx: {clima.Temperaturas.Maxima}°C\n" +
                   $"• Humedad: {clima.Humedad}% | Viento: {clima.Viento} km/h\n" +
                   $"• Precipitación: {clima.Precipitacion} mm\n" +
                   $"• Actualizado: {clima.Elaborado}\n\n" +
                   ConsejoCoche(clima.EstadoCielo.Descripcion, clima.Precipitacion);
        }

        /// <summary>
        /// Última línea del parte: si el cielo lleva agua, lavar el coche es tirar el dinero.
        /// Mira la descripción y no solo los milímetros, porque el MITECO publica "Cubierto
        /// con lluvia escasa" con 0,0 mm acumulados.
        /// </summary>
        private static string ConsejoCoche(string? descripcion, string? precipitacion) =>
            $"🚗 {(VaAGover(descripcion, precipitacion) ? "**BAJO NINGÚN CONCEPTO lave el coche**" : "**Lave el coche**")}";

        private static readonly string[] PalabrasDeAgua =
            ["lluvia", "chubasco", "tormenta", "nieve", "granizo", "aguacero", "orballo"];

        private static bool VaAGover(string? descripcion, string? precipitacion)
        {
            if (double.TryParse(precipitacion, NumberStyles.Any, CultureInfo.InvariantCulture, out var milimetros)
                && milimetros > 0)
            {
                return true;
            }

            return PalabrasDeAgua.Any(palabra =>
                descripcion?.Contains(palabra, StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>
        /// Emoticon que resume el estado del cielo. El MITECO manda texto libre ("Intervalos
        /// nubosos con lluvia escasa", "Nubes altas"), así que se decide por palabras clave.
        /// El orden importa: lo más específico antes que lo general, porque "Intervalos nubosos
        /// con lluvia escasa" tiene que salir como lluvia y no como nube.
        /// </summary>
        internal static string EmojiCielo(string? descripcion)
        {
            if (string.IsNullOrWhiteSpace(descripcion))
                return "🌤️";

            bool Dice(params string[] palabras) =>
                palabras.Any(palabra => descripcion.Contains(palabra, StringComparison.OrdinalIgnoreCase));

            if (Dice("tormenta", "torment", "chubasco", "trueno", "relámpago"))
                return "⛈️";
            if (Dice("nieve", "nevada", "nevadas", "hielo", "helada"))
                return "❄️";
            if (Dice("lluvia", "chubascos", "aguacero", "orballo", "granizo", "aguas"))
                return "🌧️";
            if (Dice("niebla", "neblina", "bruma"))
                return "🌫️";
            if (Dice("intervalos nubosos", "poco nuboso", "nubes altas", "algunas nubes"))
                return "⛅";
            if (Dice("despejado", "cielo raso", "soleado", "sin nubes", "claro"))
                return "☀️";
            if (Dice("nuboso", "nubes", "nubosa", "nublado", "nublada", "nubosidad", "cubierto"))
                return "☁️";

            return "🌤️";
        }

        /// <summary>
        /// Texto del menú de predicción: el desglose por horas del día en curso y el resumen
        /// de los días siguientes, ya normalizado por <see cref="PrediccionService"/>.
        /// </summary>
        internal async Task<string> ObtenerPrediccionAsync(CancellationToken cancellationToken)
        {
            var prediccion = await _prediccionService.ObtenerPrediccionAsync(
                MunicipioAlcala.CodigoProvincia, MunicipioAlcala.CodigoIne, cancellationToken);

            if (prediccion is null || (prediccion.Horarios.Count == 0 && prediccion.Dias.Count == 0))
                return $"⚠️ No se pudo obtener la predicción para {MunicipioAlcala.Nombre} en este momento.";

            var sb = new StringBuilder();
            sb.AppendLine($"📅 **Predicción en {Escapar(prediccion.Nombre)}**");

            sb.AppendLine();
            sb.AppendLine("🕐 **Hoy por horas**");
            if (prediccion.Horarios.Count == 0)
            {
                sb.AppendLine("Sin detalle horario disponible.");
            }
            else
            {
                foreach (var tramo in prediccion.Horarios)
                {
                    string hora = tramo.Hora is { Length: >= 2 } ? tramo.Hora[..2] : "--";
                    string cielo = Escapar(tramo.Cielo);
                    if (string.IsNullOrWhiteSpace(cielo))
                        cielo = "Variable";

                    var partes = new List<string> { $"{hora}h {EmojiCielo(cielo)} {cielo}" };
                    if (tramo.Temperatura is { } temperatura)
                        partes.Add($"{Formato(temperatura, 0)}°C");
                    if (tramo.Sensacion is { } sensacion && Math.Abs(sensacion - (tramo.Temperatura ?? sensacion)) >= 1)
                        partes.Add($"sens. {Formato(sensacion, 0)}°");
                    if (tramo.VientoDireccion is { Length: > 0 } && tramo.VientoVelocidad is { } velocidad)
                        partes.Add($"{tramo.VientoDireccion} {Formato(velocidad, 0)} km/h");

                    sb.AppendLine($"• {string.Join(" · ", partes)}");
                }
            }

            var dias = prediccion.Dias.Skip(1).ToArray();
            if (dias.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("🗓️ **Próximos días**");
                foreach (var dia in dias)
                    sb.AppendLine($"• {EtiquetaDia(dia)}");
            }

            if (!string.IsNullOrWhiteSpace(prediccion.Elaborado))
                sb.AppendLine($"\nActualizado: {Escapar(prediccion.Elaborado)}");

            return sb.ToString();
        }

        private static string EtiquetaDia(DiaPrediccion dia)
        {
            string cielo = Escapar(dia.Cielo);
            var partes = new List<string>
            {
                $"{EmojiCielo(cielo)} {FormatoDia(dia.Fecha)}"
            };

            if (dia.Maxima is { } maxima && dia.Minima is { } minima)
                partes.Add($"{Formato(minima, 0)}° / {Formato(maxima, 0)}°C");
            else if (dia.Maxima is { } soloMaxima)
                partes.Add($"máx. {Formato(soloMaxima, 0)}°C");

            if (!string.IsNullOrWhiteSpace(cielo))
                partes.Add(cielo);
            if (dia.ProbPrecipitacion is { } probabilidad)
                partes.Add($"lluvia {Formato(probabilidad, 0)}%");
            if (dia.Viento is { } viento)
                partes.Add($"viento {Formato(viento, 0)} km/h");
            if (dia.RachaMax is { } racha)
                partes.Add($"racha {Formato(racha, 0)} km/h");

            return string.Join(" · ", partes);
        }

        /// <summary>Fecha corta y legible: "vie 3 oct".</summary>
        internal static string FormatoDia(DateOnly fecha)
        {
            string dia = fecha.DayOfWeek switch
            {
                DayOfWeek.Monday => "lun",
                DayOfWeek.Tuesday => "mar",
                DayOfWeek.Wednesday => "mié",
                DayOfWeek.Thursday => "jue",
                DayOfWeek.Friday => "vie",
                DayOfWeek.Saturday => "sáb",
                _ => "dom"
            };

            return $"{dia} {fecha.Day} {fecha.ToString("MMM", CultureEspanol)}";
        }

        /// <summary>
        /// Pregunta con qué carburante comparar antes de mostrar las gasolineras. El tipo y la
        /// moneda se eligen una vez y a partir de ahí viajan en el callbackData de cada botón.
        /// El pueblo no se anuncia aquí porque todavía no se ha tirado el dado.
        /// </summary>
        internal static string PreguntaCarburante { get; } =
            "⛽ **¿Qué carburante quieres consultar?**\n\n" +
            "Te paso las gasolineras más baratas del municipio, ordenadas de más barata a más cara.";

        /// <summary>Teclado de la pregunta. La moneda solo cambia qué botones habrá después.</summary>
        internal static InlineKeyboardMarkup TecladoCarburante(Moneda moneda) =>
            new(
            [
                [new InlineKeyboardButton(TipoCarburante.Gasolina95.Boton()) { CallbackData = $"{CallbackGasolineras}|{TipoCarburante.Gasolina95.Token()}|{moneda.Token()}" }],
                [new InlineKeyboardButton(TipoCarburante.GasoleoA.Boton()) { CallbackData = $"{CallbackGasolineras}|{TipoCarburante.GasoleoA.Token()}|{moneda.Token()}" }],
                ConstruirFilaPrincipal()
            ]);

        internal static InlineKeyboardMarkup TecladoInicio() =>
            new(ConstruirFilaPrincipal());

        /// <summary>
        /// Gasolineras cerca del pueblo tocado por el dado. El pueblo llega como parámetro y no
        /// se tira aquí porque al paginar tiene que ser el mismo de la primera página. La moneda
        /// tampoco se tira: viaja en el callback para que el botón la alterne sin perder la página.
        /// </summary>
        internal async Task<(string Texto, InlineKeyboardMarkup Teclado)> ConstruirTecladoGasolinerasAsync(
            Pueblo pueblo,
            int pagina,
            TipoCarburante carburante,
            Moneda moneda,
            CancellationToken cancellationToken)
        {
            var gasolineras = await _gasolinaService.ObtenerGasolinerasCercaAsync(
                pueblo.CodProvincia,
                pueblo.IdMunicipio,
                pueblo.Latitud,
                pueblo.Longitud,
                carburante,
                cancellationToken);

            if (gasolineras.Count == 0)
                return ($"⛽ No hay gasolineras con precio de {carburante.Nombre()} en {pueblo.Nombre}.",
                        new InlineKeyboardMarkup(FilasOpciones(pueblo, pagina, carburante, moneda)));

            int totalPaginas = (int)Math.Ceiling(gasolineras.Count / (double)GasolinerasPorPagina);
            pagina = Math.Clamp(pagina, 0, totalPaginas - 1);

            var sb = new StringBuilder();
            sb.AppendLine($"⛽ **Gasolineras {pueblo.Nombre}**");
            sb.AppendLine(carburante.Titulo());

            var paginaActual = gasolineras
                .Skip(pagina * GasolinerasPorPagina)
                .Take(GasolinerasPorPagina);

            int posicion = pagina * GasolinerasPorPagina;
            foreach (var gasolinera in paginaActual)
            {
                posicion++;
                sb.AppendLine($"{posicion}. **{Escapar(gasolinera.Nombre)}** — **{FormatoPrecio(gasolinera.Precio, moneda)}**");
                sb.AppendLine($"   {Escapar(gasolinera.Direccion)}");
            }

            var filas = new List<InlineKeyboardButton[]>();
            if (totalPaginas > 1)
            {
                filas.Add(ConstruirFilaNavegacion(
                    p => $"{CallbackGasolinerasPagina}|{pueblo.CodIne}|{p}|{carburante.Token()}|{moneda.Token()}",
                    pagina,
                    totalPaginas));
            }

            filas.AddRange(FilasOpciones(pueblo, pagina, carburante, moneda));

            return (sb.ToString(), new InlineKeyboardMarkup(filas));
        }

        /// <summary>
        /// Botones de debajo del listado de gasolineras: cambiar de moneda, cambiar de
        /// carburante y volver al menú. El pueblo ya lo ha fijado el dado, así que no tiene
        /// sentido un botón para cambiar de municipio, y el clima se alcanza desde la fila
        /// principal de abajo. La moneda y el carburante viajan en el callback porque el bot
        /// es stateless: los dos botones intercambian su valor en lugar de preguntar.
        /// </summary>
        private static List<InlineKeyboardButton[]> FilasOpciones(
            Pueblo pueblo,
            int pagina,
            TipoCarburante carburante,
            Moneda moneda) =>
        [
            [
                new InlineKeyboardButton(moneda.Boton()) { CallbackData = $"{CallbackGasolinerasMoneda}|{pueblo.CodIne}|{pagina}|{carburante.Token()}|{moneda.Token()}" },
                new InlineKeyboardButton(BotonCambiarCarburante) { CallbackData = $"{CallbackGasolinerasCarburante}|{pueblo.CodIne}|{pagina}|{carburante.Token()}|{moneda.Token()}" }
            ],
            [new InlineKeyboardButton(BotonMenu) { CallbackData = CallbackMenu }],
            ConstruirFilaPrincipal()
        ];

        internal static string Formato(double valor, int decimales) => valor.ToString("F" + decimales, CultureEspanol);

        /// <summary>
        /// 1 euro son 166,386 pesetas desde 1998, así que la conversión es fija y no hace
        /// falta ir a buscar un tipo de cambio. El MITECO manda el precio en euros por litro y
        /// el bot lo enseña en pesetas o en euros, según el botón que se haya pulsado.
        /// </summary>
        internal const double PesetasPorEuro = 166.386;

        /// <summary>
        /// El precio se escribe sin la barra de "€/L": Telegram se come lo que va detrás de una
        /// barra como si fuera un comando y el mensaje llegaba con el comando colgando.
        /// </summary>
        internal static string FormatoPrecio(double eurosPorLitro, Moneda moneda) => moneda switch
        {
            Moneda.Euros => $"{Formato(eurosPorLitro, 3)} € por litro",
            _ => $"{Formato(eurosPorLitro * PesetasPorEuro, 2)} ptas por litro"
        };

        /// <summary>
        /// Los rótulos vienen del MITECO y pueden llevar acentos, puntos o barras bajas
        /// (p. ej. "E.S. MAGDAOIL", "A2D"); hay que escapar los caracteres del Markdown de Telegram.
        /// </summary>
        internal static string Escapar(string texto) => texto.Replace("_", "\\_").Replace("*", "\\*").Replace("`", "\\`").Replace("[", "\\[");

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Error durante el polling de Telegram");
            return Task.CompletedTask;
        }
    }
}