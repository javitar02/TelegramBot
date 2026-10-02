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
using WeatherTelegramBot.Services.Interfaces;

namespace WeatherTelegramBot.Services
{
    public class TelegramBotService : BackgroundService
    {
        private const int GasolinerasPorPagina = 15;

        private const string BotonClima = "☀️ Consultar Clima";
        private const string BotonGasofa = "⛽ Consultar Gasofa";
        private const string BotonMenu = "🏠 Menú principal";
        private const string BotonCambiarCarburante = "🔄 Cambiar carburante";
        private const string BotonPrediccion = "🗓️ Predicción semanal";

        private const string CallbackMenu = "menu";

        /// <summary>
        /// Clima del pueblo que se está viendo. Sin INE ("w") cuando se entra desde el menú y
        /// el dado decide el pueblo; con INE ("w|41004") al volver desde la predicción, para
        /// no cambiar de pueblo a media consulta.
        /// </summary>
        private const string CallbackClima = "w";

        /// <summary>Pregunta con qué carburante comparar, antes de mostrar las gasolineras.</summary>
        private const string CallbackElegirCarburante = "tipo";

        /// <summary>Gasolineras del municipio con el carburante elegido ("ga|95").</summary>
        private const string CallbackGasolineras = "ga";

        /// <summary>Predicción del pueblo que se está viendo ("pr|41004").</summary>
        private const string CallbackPrediccion = "pr";

        /// <summary>Paginación del listado de gasolineras ("gl|41004|1|95").</summary>
        private const string CallbackGasolinerasPagina = "gl";

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
                {
                    // Desde la fila principal no viene INE y el dado tira, pero al volver desde
                    // la predicción sí viene y hay que recuperar el pueblo que se estaba viendo.
                    var puebloDelClima = string.IsNullOrEmpty(Parte(1))
                        ? Pueblos.Elegir()
                        : Pueblos.PorIne(Parte(1));

                    await ReemplazarMensaje(
                        chatId,
                        messageId,
                        await ObtenerTiempoAsync(puebloDelClima.CodProvincia, puebloDelClima.CodIne, cancellationToken),
                        TecladoAviso(BotonPrediccion, $"{CallbackPrediccion}|{puebloDelClima.CodIne}"),
                        cancellationToken);
                    break;
                }

                case CallbackPrediccion:
                {
                    var puebloPrediccion = Pueblos.PorIne(Parte(1));
                    await ReemplazarMensaje(
                        chatId,
                        messageId,
                        await ObtenerPrediccionAsync(puebloPrediccion, cancellationToken),
                        TecladoAviso("🌤️ Volver al clima", $"{CallbackClima}|{puebloPrediccion.CodIne}"),
                        cancellationToken);
                    break;
                }

                case CallbackGasolineras:
                case CallbackGasolinerasPagina:
                case CallbackGasolinerasCarburante:
                    // El bot es stateless, así que el carburante viaja en el propio callback:
                    // "ga|95" arranca el listado, "gl|41004|1|95" lo pagina y "gc|41004|1|95"
                    // cambia el carburante. Las dos últimas dejan el resto del listado intacto.
                    // "ga" es la única acción corta: no lleva INE ni página, así que sus campos
                    // van desplazados una posición respecto a las otras.
                    bool esContinuacion = accion != CallbackGasolineras;
                    var tipoElegido = TipoCarburanteExtensions.DesdeToken(Parte(esContinuacion ? 3 : 1));
                    if (tipoElegido is not { } tipo)
                    {
                        await ReemplazarMensaje(chatId, messageId, PreguntaCarburante, TecladoCarburante(), cancellationToken);
                        break;
                    }

                    // Al arrancar se tira el dado, pero al paginar manda el INE del callback:
                    // si no, la página 2 podría salir de un pueblo distinto al de la página 1.
                    var pueblo = esContinuacion ? Pueblos.PorIne(Parte(1)) : Pueblos.Elegir();

                    int pagina = esContinuacion && int.TryParse(Parte(2), out int gl) ? gl : 0;

                    // El botón de carburante no pregunta: directamente invierte el que hay
                    // en pantalla, así que el listado sale ya con el otro combustible.
                    if (accion == CallbackGasolinerasCarburante)
                        tipo = tipo.Contrario();

                    var gasolineras = await ConstruirTecladoGasolinerasAsync(pueblo, pagina, tipo, cancellationToken);
                    await ReemplazarMensaje(chatId, messageId, gasolineras.Texto, gasolineras.Teclado, cancellationToken, ParseMode.Html);
                    break;

                case CallbackElegirCarburante:
                    await ReemplazarMensaje(chatId, messageId, PreguntaCarburante, TecladoCarburante(), cancellationToken);
                    break;
            }

            await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
        }

        private async Task ReemplazarMensaje(
            long chatId,
            int messageId,
            string texto,
            InlineKeyboardMarkup teclado,
            CancellationToken cancellationToken,
            ParseMode parseMode = ParseMode.Markdown)
        {
            try
            {
                await _botClient.EditMessageText(
                    chatId: chatId,
                    messageId: messageId,
                    text: texto,
                    parseMode: parseMode,
                    replyMarkup: teclado,
                    cancellationToken: cancellationToken
                );
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
            {
                // Telegram responde 400 si el teclado y el texto no han cambiado: es inocuo.
            }
        }

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

        internal async Task<string> ObtenerTiempoAsync(
            string codProvincia,
            string codIne,
            CancellationToken cancellationToken)
        {
            var clima = await _weatherService.ObtenerTiempoPorMunicipioAsync(codProvincia, codIne, cancellationToken);

            if (clima?.Municipio is null || clima.Temperaturas is null || clima.EstadoCielo is null)
                return "⚠️ No se pudo obtener la información del clima en este momento.";

            var sb = new StringBuilder();
            sb.AppendLine($"{EmojiCielo(clima.EstadoCielo.Descripcion)} **El tiempo en {clima.Municipio.Nombre}** ({clima.Municipio.NombreProvincia})");
            sb.AppendLine();
            sb.AppendLine($"• Estado: {clima.EstadoCielo.Descripcion}");
            sb.AppendLine($"• Actual: {clima.TemperaturaActual}°C | Mín: {clima.Temperaturas.Minima}°C | Máx: {clima.Temperaturas.Maxima}°C");
            sb.AppendLine($"• Humedad: {clima.Humedad}% | Viento: {clima.Viento} km/h");
            sb.AppendLine($"• Precipitación: {clima.Precipitacion} mm");

            return sb.ToString();
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

        /// <summary>Encabezado del mensaje de predicción.</summary>
        private static string TituloPrediccion =>
            "🗓️ **PREDICCIÓN SEMANAL**";

        /// <summary>
        /// Mensaje de la predicción semanal, en su propia pantalla: un día por bloque, con el
        /// icono junto al nombre del día y los datos debajo. Va separado del parte del clima
        /// porque son dos consultas distintas y así el botón de atrás va a donde toca.
        /// </summary>
        /// <param name="pueblo">
        /// El mismo pueblo que sale en el parte del clima, que viaja en el callback: si la
        /// predicción saliera de Alcalá y el clima de otro sitio, el botón de "volver" rompería.
        /// </param>
        internal async Task<string> ObtenerPrediccionAsync(Pueblo pueblo, CancellationToken cancellationToken)
        {
            var prediccion = await _prediccionService.ObtenerPrediccionAsync(
                pueblo.CodProvincia, pueblo.CodIne, cancellationToken);

            if (prediccion is null || prediccion.Dias.Count == 0)
                return $"⚠️ No se pudo obtener la predicción para {pueblo.Nombre} en este momento.";

            var dias = prediccion.Dias.Skip(1).ToArray();

            var sb = new StringBuilder();
            sb.AppendLine(TituloPrediccion);
            sb.AppendLine($"en {Escapar(prediccion.Nombre)}");

            foreach (var dia in dias)
            {
                sb.AppendLine();
                sb.Append(EtiquetaDia(dia));
            }

            // Con dos o más días mojados la semana ya no da ventana: el aviso va al final,
            // separado de los bloques, para que se lea como conclusión y no como otro día.
            if (dias.Count(DiaMojado) >= MinimosDiasMojados)
            {
                sb.AppendLine();
                sb.AppendLine(ConsejoLavarCoche);
            }

            return sb.ToString();
        }

        /// <summary>Umbral de probabilidad a partir del cual el día cuenta como mojado.</summary>
        private const double UmbralLluvia = 60;

        /// <summary>Días con lluvia probable a partir de los cuales no merece la pena lavar.</summary>
        private const int MinimosDiasMojados = 2;

        private const string ConsejoLavarCoche = "🚗 **Se recomienda lavar el coche**";

        private static bool DiaMojado(DiaPrediccion dia) => dia.ProbPrecipitacion is { } probabilidad && probabilidad > UmbralLluvia;

        private static string EtiquetaDia(DiaPrediccion dia)
        {
            string cielo = Escapar(dia.Cielo);
            var sb = new StringBuilder();

            sb.AppendLine($"{EmojiCielo(cielo)} **{FormatoDiaCompleto(dia.Fecha)}**");

            if (dia.Maxima is { } maxima && dia.Minima is { } minima)
            {
                sb.AppendLine($"• Máxima: {Formato(maxima, 0)}°C");
                sb.AppendLine($"• Mínima: {Formato(minima, 0)}°C");
            }
            else if (dia.Maxima is { } soloMaxima)
            {
                sb.AppendLine($"• Máxima: {Formato(soloMaxima, 0)}°C");
            }
            else if (dia.Minima is { } soloMinima)
            {
                sb.AppendLine($"• Mínima: {Formato(soloMinima, 0)}°C");
            }

            if (dia.ProbPrecipitacion is { } probabilidad)
                sb.AppendLine($"• Probabilidad de lluvia: {Formato(probabilidad, 0)}%");

            if (dia.Viento is { } viento)
                sb.AppendLine($"• Viento: {Formato(viento, 0)} km/h");
            if (dia.RachaMax is { } racha)
                sb.AppendLine($"• Racha máxima: {Formato(racha, 0)} km/h");

            return sb.ToString();
        }

        /// <summary>Fecha completa y legible: "Viernes 2 de octubre".</summary>
        internal static string FormatoDiaCompleto(DateOnly fecha)
        {
            string diaSemana = fecha.DayOfWeek switch
            {
                DayOfWeek.Monday => "Lunes",
                DayOfWeek.Tuesday => "Martes",
                DayOfWeek.Wednesday => "Miércoles",
                DayOfWeek.Thursday => "Jueves",
                DayOfWeek.Friday => "Viernes",
                DayOfWeek.Saturday => "Sábado",
                _ => "Domingo"
            };

            string mes = fecha.ToString("MMMM", CultureEspanol);
            return $"{diaSemana} {fecha.Day} de {mes}";
        }

        /// <summary>
        /// Pregunta con qué carburante comparar antes de mostrar las gasolineras. El tipo se
        /// elige una vez y a partir de ahí viaja en el callbackData de cada botón. El pueblo
        /// no se anuncia aquí porque todavía no se ha tirado el dado.
        /// </summary>
        internal static string PreguntaCarburante { get; } =
            "⛽ **¿Qué carburante quieres consultar?**\n\n" +
            "Te paso las gasolineras más baratas del municipio, ordenadas de más barata a más cara.";

        /// <summary>Teclado de la pregunta de carburante.</summary>
        internal static InlineKeyboardMarkup TecladoCarburante() =>
            new(
            [
                [new InlineKeyboardButton(TipoCarburante.Gasolina95.Boton()) { CallbackData = $"{CallbackGasolineras}|{TipoCarburante.Gasolina95.Token()}" }],
                [new InlineKeyboardButton(TipoCarburante.GasoleoA.Boton()) { CallbackData = $"{CallbackGasolineras}|{TipoCarburante.GasoleoA.Token()}" }],
                ConstruirFilaPrincipal()
            ]);

        internal static InlineKeyboardMarkup TecladoInicio() =>
            new(ConstruirFilaPrincipal());

        /// <summary>
        /// Gasolineras cerca del pueblo tocado por el dado. El pueblo llega como parámetro y no
        /// se tira aquí porque al paginar tiene que ser el mismo de la primera página.
        /// </summary>
        internal async Task<(string Texto, InlineKeyboardMarkup Teclado)> ConstruirTecladoGasolinerasAsync(
            Pueblo pueblo,
            int pagina,
            TipoCarburante carburante,
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
                        new InlineKeyboardMarkup(FilasOpciones(pueblo, pagina, carburante)));

            int totalPaginas = (int)Math.Ceiling(gasolineras.Count / (double)GasolinerasPorPagina);
            pagina = Math.Clamp(pagina, 0, totalPaginas - 1);

            var sb = new StringBuilder();
            sb.AppendLine($"⛽ <b>Gasolineras {EscaparHtml(pueblo.Nombre)}</b>");
            sb.AppendLine(carburante.Titulo());

            var paginaActual = gasolineras
                .Skip(pagina * GasolinerasPorPagina)
                .Take(GasolinerasPorPagina);

            int posicion = pagina * GasolinerasPorPagina;
            foreach (var gasolinera in paginaActual)
            {
                posicion++;
                sb.AppendLine($"{posicion}. <b>{EscaparHtml(gasolinera.Nombre)}</b> — <b>{FormatoPrecio(gasolinera.Precio)}</b>");
                sb.AppendLine($"   {EnlaceMapa(gasolinera)}");
            }

            var filas = new List<InlineKeyboardButton[]>();
            if (totalPaginas > 1)
            {
                filas.Add(ConstruirFilaNavegacion(
                    p => $"{CallbackGasolinerasPagina}|{pueblo.CodIne}|{p}|{carburante.Token()}",
                    pagina,
                    totalPaginas));
            }

            filas.AddRange(FilasOpciones(pueblo, pagina, carburante));

            return (sb.ToString(), new InlineKeyboardMarkup(filas));
        }

        /// <summary>
        /// La dirección se muestra como enlace a Google Maps. Se busca por coordenadas y no
        /// por la dirección textual porque las del feed vienen sin número en muchos casos y
        /// el mapa acabaría en la calle equivocada. El formato de URL es el oficial de Google
        /// Maps y abre la app en el móvil en lugar del navegador.
        /// </summary>
        private static string EnlaceMapa(Gasolinera gasolinera)
        {
            string coordenadas = $"{gasolinera.Latitud.ToString(CultureInfo.InvariantCulture)}," +
                                 $"{gasolinera.Longitud.ToString(CultureInfo.InvariantCulture)}";

            string url = $"https://maps.google.com/?q={coordenadas}";

            // Sin dirección no hay línea que enseñar: mejor un enlace vacío que un underline
            // sin destino, así que se cae al rótulo de la estación.
            string etiqueta = string.IsNullOrWhiteSpace(gasolinera.Direccion)
                ? $"📍 {EscaparHtml(gasolinera.Nombre)}"
                : EscaparHtml(gasolinera.Direccion);

            return $"<a href=\"{url}\">{etiqueta}</a>";
        }

        /// <summary>
        /// El listado de gasolineras se manda en HTML y no en Markdown porque el Markdown
        /// clásico de Telegram no sabe hyperlinkear. Aquí hay que escapar los tags.
        /// </summary>
        internal static string EscaparHtml(string texto) =>
            texto.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        /// <summary>
        /// Botones de debajo del listado de gasolineras: cambiar de carburante y volver al
        /// menú. El pueblo ya lo ha fijado el dado, así que no tiene sentido un botón para
        /// cambiar de municipio, y el clima se alcanza desde la fila principal de abajo. El
        /// carburante viaja en el callback porque el bot es stateless: el botón invierte el
        /// valor en lugar de preguntar.
        /// </summary>
        private static List<InlineKeyboardButton[]> FilasOpciones(
            Pueblo pueblo,
            int pagina,
            TipoCarburante carburante) =>
        [
            [
                new InlineKeyboardButton(BotonCambiarCarburante) { CallbackData = $"{CallbackGasolinerasCarburante}|{pueblo.CodIne}|{pagina}|{carburante.Token()}" }
            ],
            [new InlineKeyboardButton(BotonMenu) { CallbackData = CallbackMenu }],
            ConstruirFilaPrincipal()
        ];

        internal static string Formato(double valor, int decimales) => valor.ToString("F" + decimales, CultureEspanol);

        /// <summary>
        /// 1 euro son 166,386 pesetas desde 1998, así que la conversión es fija y no hace
        /// falta ir a buscar un tipo de cambio. El MITECO manda el precio en euros por litro y
        /// el bot lo enseña siempre en pesetas.
        /// </summary>
        internal const double PesetasPorEuro = 166.386;

        /// <summary>
        /// El precio se escribe sin la barra de "€/L": Telegram se come lo que va detrás de una
        /// barra como si fuera un comando y el mensaje llegaba con el comando colgando.
        /// </summary>
        internal static string FormatoPrecio(double eurosPorLitro) =>
            $"{Formato(eurosPorLitro * PesetasPorEuro, 2)} ptas por litro";

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