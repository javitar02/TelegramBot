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
        /// <summary>
        /// Gasolineras que caben en un mensaje. El listado no se pagina: se enseña este tope y
        /// se avisa de cuántas se quedan fuera.
        /// </summary>
        private const int GasolinerasPorPagina = 15;

        private const string BotonClima = "☀️ Consultar Clima";
        private const string BotonGasofa = "⛽ Consultar Gasofa";
        private const string BotonRepostar = "⛽ Repostar";
        private const string BotonMenu = "🏠 Menú principal";
        private const string BotonCambiarPueblo = "🔄 Cambiar pueblo";
        private const string BotonCambiarCarburante = "🔄 Cambiar carburante";

        /// <summary>
        /// Vuelve al mensaje de bienvenida. Es la única salida del flujo de gasofa, así que
        /// los teclados de ese flujo lo llevan en vez de la fila de acceso directo.
        /// </summary>
        private const string CallbackMenu = "menu";

        /// <summary>
        /// Pide el siguiente pueblo de la rotación y vuelve a pintar su parte del tiempo
        /// ("cp|41004"). El INE es el del pueblo que se está viendo, que es lo único que
        /// permite saber cuál toca: el bot es stateless y no lleva ningún contador.
        /// </summary>
        private const string CallbackCambiarPueblo = "cp";

        /// <summary>
        /// Clima del pueblo que se está viendo. Sin INE ("w") cuando se entra desde el menú y
        /// arranca la rotación; con INE ("w|41004") al volver desde otro pueblo.
        /// </summary>
        private const string CallbackClima = "w";

        /// <summary>Pregunta con qué carburante comparar, antes de mostrar las gasolineras.</summary>
        private const string CallbackElegirCarburante = "tipo";

        /// <summary>Gasolineras del municipio con el carburante elegido ("ga|95").</summary>
        private const string CallbackGasolineras = "ga";

        /// <summary>
        /// Pasa de un carburante al otro directamente, sin volver a preguntar: el botón sabe
        /// por el callback cuál se está viendo y solo tiene que invertirlo.
        /// </summary>
        private const string CallbackGasolinerasCarburante = "gc";

        /// <summary>
        /// Cambia de municipio sin salir del listado ("gp|41004|95"). El INE que viaja es el
        /// del pueblo que se está viendo, para saber cuál es el siguiente de la rotación: el
        /// handler lo avanza y reconstruye el listado con el mismo carburante.
        /// </summary>
        private const string CallbackGasolinerasPueblo = "gp";

        /// <summary>Abre el repostaje y pregunta cuánto se va a meter ("rp").</summary>
        private const string CallbackRepostar = "rp";

        /// <summary>
        /// Elige una de las cantidades ofrecidas ("ro|2"). El índice viaja en vez del texto
        /// porque el callbackData tiene 64 bytes de límite y los rótulos no caben.
        /// </summary>
        private const string CallbackRepostarElegir = "ro";

        /// <summary>
        /// Cantidades que se ofrecen al repostar. Van en un array y no en un diccionario porque
        /// el callbackData solo lleva el índice: Telegram lo limita a 64 bytes y los rótulos son
        /// largos. Lo que haga cada una todavía no está escrito, así que aquí solo hay el texto.
        /// </summary>
        private static readonly string[] OpcionesReposto =
        [
            "Yo siempre le echo 20€",
            "Llenaso gordo",
            "Sinco Euritos",
            "Paga tú que a mí me da la risa",
        ];

        /// <summary>
        /// Posición en <see cref="OpcionesReposto"/> de la opción que responde con una imagen.
        /// Al ir por posición, reordenar el array cambia también lo que hace el botón.
        /// </summary>
        private const int IndiceNoLlennes = 1;

        /// <summary>Carpeta del proyecto donde están las imágenes del repostaje.</summary>
        private const string CarpetaImagenes = "img";

        private const string ImagenNoLlennes = "noLlenesDefinitiva.png";

        /// <summary>Mensaje de cierre del repostaje, con el botón de vuelta al menú.</summary>
        private const string RepostajeFinalizado = "\U0001F6E1 *Repostaje Finalizado*";

        private const string PieNoLlennes =
            "*QUE NO LLENES QUE NO SUBE COÑO*\n\n\U0001F451 Rufino I de Portugal\n\n"
            + "\"Estas fueron las sabias palabras del maestro Moreno Pacheco antes de la catástrofe de las gasofas. ¿Ha elegido usted una sabia decisión? Solo el tiempo dirá...\"";

        // En los custom format de .NET la coma es separador de millares, no decimal:
        // hay que pedir la cultura española y usar F1/F2/F3 para obtener "3,4" y "1,799".
        /// <summary>
        /// Cantidad de ids que se borran de una vez. Es el tope que acepta la API de Telegram
        /// para deleteMessages, así que el chat se limpia en tandas y no mensaje a mensaje.
        /// </summary>
        private const int MensajesPorTanda = 100;

        private static readonly CultureInfo CultureEspanol = CultureInfo.GetCultureInfo("es-ES");

        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<TelegramBotService> _logger;
        private readonly IWeatherService _weatherService;
        private readonly IGasolinaService _gasolinaService;
        private readonly IPrediccionService _prediccionService;
        private readonly IHostEnvironment _entorno;

        public TelegramBotService(
            ITelegramBotClient botClient,
            IWeatherService weatherService,
            IGasolinaService gasolinaService,
            IPrediccionService prediccionService,
            IHostEnvironment entorno,
            ILogger<TelegramBotService> logger)
        {
            _botClient = botClient;
            _weatherService = weatherService;
            _gasolinaService = gasolinaService;
            _prediccionService = prediccionService;
            _entorno = entorno;
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
            $"Elige el clima o los precios de la gasolina ⛽";

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
                case CallbackMenu:
                    // Al volver al menú se vacía el chat entero y se manda el mensaje de
                    // bienvenida nuevo, en vez de editar el que había: así el usuario no
                    // ve el historial de consultas que ha ido dejando.
                    await VaciarChatAsync(chatId, messageId, cancellationToken);
                    await EnviarMenu(_botClient, chatId, cancellationToken);
                    break;

                case CallbackCambiarPueblo:
                    // El pueblo nuevo se elige ahora, no al pintar: a partir de aquí su INE
                    // viaja en todos los botones, así que la predicción que se lee debajo del
                    // clima y el resto de consultas salen de él sin volver a avanzar la rotación.
                    var puebloNuevo = Pueblos.Siguiente(Parte(1));
                    await ReemplazarMensaje(
                        chatId,
                        messageId,
                        await ObtenerTiempoAsync(puebloNuevo, cancellationToken),
                        TecladoClima(puebloNuevo),
                        cancellationToken);
                    break;

                case CallbackClima:
                {
                    // Desde la fila principal no viene INE y arranca la rotación, pero al volver
                    // desde el cambio de pueblo sí viene y hay que recuperar el que se estaba viendo.
                    var puebloDelClima = string.IsNullOrEmpty(Parte(1))
                        ? Pueblos.Inicial
                        : Pueblos.PorIne(Parte(1));

                    await ReemplazarMensaje(
                        chatId,
                        messageId,
                        await ObtenerTiempoAsync(puebloDelClima, cancellationToken),
                        TecladoClima(puebloDelClima),
                        cancellationToken);
                    break;
                }

                case CallbackGasolineras:
                case CallbackGasolinerasCarburante:
                case CallbackGasolinerasPueblo:
                    // El bot es stateless, así que el carburante viaja en el propio callback:
                    // "ga|95" arranca el listado, "gc|41004|95" cambia el carburante y
                    // "gp|41004|95" cambia de municipio, sin tocar el resto del listado. "ga" es
                    // la única acción corta: no lleva INE, así que sus campos van desplazados
                    // una posición respecto a las otras dos.
                    bool esContinuacion = accion != CallbackGasolineras;
                    var tipoElegido = TipoCarburanteExtensions.DesdeToken(Parte(esContinuacion ? 2 : 1));
                    if (tipoElegido is not { } tipo)
                    {
                        await ReemplazarMensaje(chatId, messageId, PreguntaCarburante, TecladoCarburante(), cancellationToken);
                        break;
                    }

                    // Al arrancar se empieza por el primero de la rotación, pero en las otras
                    // dos manda el INE del callback: si no, el listado podría cambiar de
                    // pueblo a media consulta.
                    var pueblo = esContinuacion ? Pueblos.PorIne(Parte(1)) : Pueblos.Inicial;

                    // El botón de carburante no pregunta: directamente invierte el que hay
                    // en pantalla, así que el listado sale ya con el otro combustible.
                    if (accion == CallbackGasolinerasCarburante)
                        tipo = tipo.Contrario();

                    // El de pueblo tampoco: se pide el siguiente de la rotación, deduciéndolo
                    // del INE del callback.
                    if (accion == CallbackGasolinerasPueblo)
                        pueblo = Pueblos.Siguiente(pueblo.CodIne);

                    var gasolineras = await ConstruirTecladoGasolinerasAsync(pueblo, tipo, cancellationToken);
                    await ReemplazarMensaje(chatId, messageId, gasolineras.Texto, gasolineras.Teclado, cancellationToken, ParseMode.Html);
                    break;

                case CallbackRepostar:
                    await ReemplazarMensaje(chatId, messageId, PreguntaReposto, TecladoReposto(), cancellationToken);
                    break;

                case CallbackRepostarElegir:
                {
                    // Cada opción tiene su propio destino; solo está escrita la del "llenaso
                    // gordo". Las demás reconocen el callback y dejan la pantalla como está.
                    if (int.TryParse(Parte(1), out int indice) && indice == IndiceNoLlennes)
                        await EnviarNoLlennesAsync(chatId, messageId, cancellationToken);

                    break;
                }

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
        /// Vacía el chat borrando todos los mensajes hasta <paramref name="ultimoMessageId"/>, que
        /// es el mensaje que tiene el botón de menú. Los ids van del 1 al último en tandas de
        /// <see cref="MensajesPorTanda"/>: Telegram salta los que no encuentra, así que no hace
        /// falta saber qué mensajes son del bot y cuáles del usuario, ni llevar registro de ellos.
        /// Se empieza por el final y se para en la primera tanda que falla: si una no se puede
        /// borrar es por tener más de 48 horas (el límite de Telegram) y todas las anteriores
        /// también se quedan fuera.
        /// </summary>
        private async Task VaciarChatAsync(long chatId, int ultimoMessageId, CancellationToken cancellationToken)
        {
            for (int fin = ultimoMessageId; fin > 0; fin -= MensajesPorTanda)
            {
                int primero = Math.Max(1, fin - MensajesPorTanda + 1);

                try
                {
                    await _botClient.DeleteMessages(
                        chatId: chatId,
                        messageIds: Enumerable.Range(primero, fin - primero + 1),
                        cancellationToken: cancellationToken
                    );
                }
                catch (ApiRequestException ex)
                {
                    _logger.LogInformation(
                        ex,
                        "Se deja de limpiar el chat {ChatId} a partir del mensaje {MessageId}: probably superen las 48 horas",
                        chatId,
                        primero
                    );
                    return;
                }
            }
        }

        /// <summary>
        /// Fila de acceso directo del mensaje de bienvenida a las tres consultas. Sustituye al
        /// reply keyboard: los botones inline no desaparecen al enviar otro mensaje. Cada
        /// pantalla posterior lleva solo lo suyo, sin repetir estas tres.
        /// </summary>
        private static InlineKeyboardButton[] ConstruirFilaPrincipal() =>
        [
            new InlineKeyboardButton(BotonClima) { CallbackData = CallbackClima },
            new InlineKeyboardButton(BotonGasofa) { CallbackData = CallbackElegirCarburante },
            new InlineKeyboardButton(BotonRepostar) { CallbackData = CallbackRepostar }
        ];

        /// <summary>
        /// Teclado del parte del tiempo: el cambio de pueblo y el salto a la gasofa. El botón
        /// de clima desaparece porque el clima ya está en pantalla: volver a consultarlo solo
        /// volvería a arrancar la rotación. El pueblo viaja en el callback del botón de
        /// cambiar, que es lo que le dice al handler cuál es el siguiente.
        /// </summary>
        private static InlineKeyboardMarkup TecladoClima(Pueblo pueblo) =>
            new(
            [
                [
                    new InlineKeyboardButton(BotonCambiarPueblo) { CallbackData = $"{CallbackCambiarPueblo}|{pueblo.CodIne}" },
                    new InlineKeyboardButton(BotonGasofa) { CallbackData = CallbackElegirCarburante }
                ]
            ]);

internal async Task<string> ObtenerTiempoAsync(Pueblo pueblo, CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            await AnotarTiempoAsync(sb, pueblo, cancellationToken);
            await AnotarPrediccionAsync(sb, pueblo, cancellationToken);

            return sb.ToString();
        }

        /// <summary>
        /// La parte de hoy: estado, temperaturas, viento y lluvia. Es lo único que se pinta si
        /// el MITECO no contesta, por eso no aborta el mensaje entero.
        /// </summary>
        private async Task AnotarTiempoAsync(StringBuilder sb, Pueblo pueblo, CancellationToken cancellationToken)
        {
            var clima = await _weatherService.ObtenerTiempoPorMunicipioAsync(pueblo.CodProvincia, pueblo.CodIne, cancellationToken);

            if (clima?.Municipio is null || clima.Temperaturas is null || clima.EstadoCielo is null)
            {
                sb.AppendLine($"⚠️ No se pudo obtener la información del clima de {pueblo.NombreConProvincia} en este momento.");
                return;
            }

            sb.AppendLine($"{EmojiCielo(clima.EstadoCielo.Descripcion)} **El tiempo en {clima.Municipio.Nombre}** ({clima.Municipio.NombreProvincia})");
            sb.AppendLine();
            sb.AppendLine($"• Estado: {clima.EstadoCielo.Descripcion}");
            sb.AppendLine($"• Actual: {clima.TemperaturaActual}°C | Mín: {clima.Temperaturas.Minima}°C | Máx: {clima.Temperaturas.Maxima}°C");
            sb.AppendLine($"• Humedad: {clima.Humedad}% | Viento: {clima.Viento} km/h");
            sb.AppendLine($"• Precipitación: {clima.Precipitacion} mm");
        }

        /// <summary>
        /// La semana, un día por bloque con el icono junto al nombre del día y los datos debajo.
        /// Hoy no se repite: sus temperaturas ya están en la parte de arriba. Si el MITECO no
        /// publica predicción, el parte del clima sigue valiendo y no se avisa de nada.
        /// </summary>
        private async Task AnotarPrediccionAsync(StringBuilder sb, Pueblo pueblo, CancellationToken cancellationToken)
        {
            var prediccion = await _prediccionService.ObtenerPrediccionAsync(
                pueblo.CodProvincia, pueblo.CodIne, cancellationToken);

            var dias = prediccion?.Dias.Skip(1).ToArray() ?? [];
            if (dias.Length == 0)
                return;

            sb.AppendLine();
            sb.AppendLine(TituloPrediccion);

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

        /// <summary>Encabezado del bloque de predicción, dentro del parte del tiempo.</summary>
        private static string TituloPrediccion =>
            "🗓️ **PREDICCIÓN SEMANAL**";

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
        /// no se anuncia aquí porque todavía no se ha entrado en la rotación.
        /// </summary>
        internal static string PreguntaCarburante { get; } =
            "⛽ **¿Qué carburante quieres consultar?**\n\n" +
            "Te paso las gasolineras más baratas del municipio, ordenadas de más barata a más cara.";

        /// <summary>
        /// Teclado de la pregunta de carburante: solo los dos botones, sin salida al menú. Todavía
        /// no se ha entrado en el flujo de gasofa, así que volver atrás no tiene sentido: o se
        /// elige un combustible o no hay nada que mostrar.
        /// </summary>
        internal static InlineKeyboardMarkup TecladoCarburante() =>
            new(
            [
                [new InlineKeyboardButton(TipoCarburante.Gasolina95.Boton()) { CallbackData = $"{CallbackGasolineras}|{TipoCarburante.Gasolina95.Token()}" }],
                [new InlineKeyboardButton(TipoCarburante.GasoleoA.Boton()) { CallbackData = $"{CallbackGasolineras}|{TipoCarburante.GasoleoA.Token()}" }]
            ]);

        internal static InlineKeyboardMarkup TecladoInicio() =>
            new(ConstruirFilaPrincipal());

        /// <summary>
        /// Repostaje: se pregunta cuánto se va a meter y el usuario elige entre las opciones.
        /// </summary>
        internal static string PreguntaReposto =>
            "⛽ **Repostar**\n\n" +
            "Dime cuánto le vas a poner al coche y te digo cuántos litros te entran:";

        /// <summary>Las cuatro opciones, una por fila.</summary>
        private static InlineKeyboardMarkup TecladoReposto() =>
            new([.. OpcionesReposto.Select((opcion, indice) =>
                new[]
                {
                    new InlineKeyboardButton(opcion) { CallbackData = $"{CallbackRepostarElegir}|{indice}" }
                })]);

        /// <summary>Teclado con un único botón de salida al menú de bienvenida.</summary>
        private static InlineKeyboardMarkup TecladoMenu() =>
            new(
            [
                [new InlineKeyboardButton(BotonMenu) { CallbackData = CallbackMenu }]
            ]);

        /// <summary>
        /// La respuesta al "llenaso gordo": la foto con el texto debajo. Telegram no deja editar
        /// un texto y convertirlo en imagen, así que la pantalla de opciones se borra y la foto
        /// ocupa su lugar.
        /// </summary>
        private async Task EnviarNoLlennesAsync(long chatId, int messageId, CancellationToken cancellationToken)
        {
            string ruta = Path.Combine(_entorno.ContentRootPath, CarpetaImagenes, ImagenNoLlennes);

            // La imagen no siempre está desplegada, así que si falta se manda solo
            // el texto en vez de romper la consulta.
            if (!File.Exists(ruta))
            {
                _logger.LogWarning("No se encuentra la imagen {Ruta} del repostaje", ruta);
                await ReemplazarMensaje(chatId, messageId, PieNoLlennes, TecladoMenu(), cancellationToken);
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: RepostajeFinalizado,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: TecladoMenu(),
                    cancellationToken: cancellationToken
                );
                return;
            }

            await using Stream imagen = File.OpenRead(ruta);

            await _botClient.SendPhoto(
                chatId: chatId,
                photo: new InputFileStream(imagen, ImagenNoLlennes),
                caption: PieNoLlennes,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken
            );

            await _botClient.DeleteMessage(
                chatId: chatId,
                messageId: messageId,
                cancellationToken: cancellationToken
            );

            await _botClient.SendMessage(
                chatId: chatId,
                text: RepostajeFinalizado,
                parseMode: ParseMode.Markdown,
                replyMarkup: TecladoMenu(),
                cancellationToken: cancellationToken
            );
        }

        /// <summary>
        /// Gasolineras cerca del pueblo tocado por el dado. El pueblo llega como parámetro y no
        /// se tira aquí porque tiene que ser el mismo al cambiar de carburante o de municipio.
        /// Solo se enseñan las <see cref="GasolinerasPorPagina"/> primeras, que ya vienen
        /// ordenadas de más barata a más cara, y todas en un bloque: una línea por estación con
        /// el nombre, el precio y el enlace al mapa. Cabe de sobra en un solo mensaje y así el
        /// usuario compara de un vistazo sin ir paginando.
        /// </summary>
        internal async Task<(string Texto, InlineKeyboardMarkup Teclado)> ConstruirTecladoGasolinerasAsync(
            Pueblo pueblo,
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
                return ($"⛽ No hay gasolineras con precio de {carburante.Nombre()} en {pueblo.NombreConProvincia}.",
                        new InlineKeyboardMarkup(FilasOpciones(pueblo, carburante)));

            var sb = new StringBuilder();
            sb.AppendLine($"{carburante.Emoji()} <b>{carburante.Encabezado()} {EscaparHtml(pueblo.NombreConProvincia)}</b>");

            var mostradas = gasolineras.Take(GasolinerasPorPagina).ToArray();

            for (int i = 0; i < mostradas.Length; i++)
            {
                var gasolinera = mostradas[i];
                sb.AppendLine($"{i + 1}. <b>{EscaparHtml(gasolinera.Nombre)}</b> — <b>{FormatoPrecio(gasolinera.Precio)}</b> — {EnlaceMapa(gasolinera)}");
            }

            return (sb.ToString(), new InlineKeyboardMarkup(FilasOpciones(pueblo, carburante)));
        }

        /// <summary>
        /// La dirección se muestra como enlace a Google Maps. Se busca por coordenadas y no
        /// por la dirección textual porque las del feed vienen sin número en muchos casos y
        /// el mapa acabaría en la calle equivocada. El zoom fija la vista a nivel de calle,
        /// que es lo que sirve para encontrar una gasolinera.
        /// </summary>
        private static string EnlaceMapa(Gasolinera gasolinera)
        {
            string coordenadas = $"{gasolinera.Latitud.ToString(CultureInfo.InvariantCulture)}," +
                                 $"{gasolinera.Longitud.ToString(CultureInfo.InvariantCulture)}";

            // URL de la API de Maps, no una búsqueda a pelo: "maps.google.com/?q=lat,lon" abre
            // el buscador con el rótulo "Find local business...", mientras que el endpoint de
            // /maps/search con api=1 va directo al punto. Los "and" van escapados porque la URL
            // se mete en un atributo href y Telegram no interpreta HTML completo.
            string url = $"https://www.google.com/maps/search/?api=1&amp;query={coordenadas}&amp;zoom=17";

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
        /// Botones de debajo del listado de gasolineras: cambiar de carburante, cambiar de
        /// pueblo, repostar y volver al menú. Carburante y pueblo viajan en el callback porque
        /// el bot es stateless: los botones intercambian el valor en lugar de preguntar,
        /// dejando el resto intacto.
        /// </summary>
        private static List<InlineKeyboardButton[]> FilasOpciones(
            Pueblo pueblo,
            TipoCarburante carburante) =>
        [
            [
                new InlineKeyboardButton(BotonCambiarCarburante) { CallbackData = $"{CallbackGasolinerasCarburante}|{pueblo.CodIne}|{carburante.Token()}" },
                new InlineKeyboardButton(BotonCambiarPueblo) { CallbackData = $"{CallbackGasolinerasPueblo}|{pueblo.CodIne}|{carburante.Token()}" }
            ],
            [
                new InlineKeyboardButton(BotonRepostar) { CallbackData = CallbackRepostar },
                new InlineKeyboardButton(BotonMenu) { CallbackData = CallbackMenu }
            ]
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