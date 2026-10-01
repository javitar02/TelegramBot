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
        private const double RadioGasolinerasKm = 10;

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

        /// <summary>Gasolineras de Alcalá con el carburante elegido ("ga|95").</summary>
        private const string CallbackGasolineras = "ga";

        private const string CallbackPrediccion = "pr";

        /// <summary>Paginación del listado de gasolineras ("gl|41004|1|95").</summary>
        private const string CallbackGasolinerasPagina = "gl";

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
            string valor = partes.Length > 1 ? partes[1] : "";
            string valor2 = partes.Length > 2 ? partes[2] : "";
            string valor3 = partes.Length > 3 ? partes[3] : "";

            switch (accion)
            {
                case "noop":
                    await _botClient.AnswerCallbackQuery(callbackQuery.Id, text: $"Ya estás en la página {valor}.", cancellationToken: cancellationToken);
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
                    // El bot es stateless, así que el carburante viaja en el propio callback:
                    // "ga|95" arranca el listado y "gl|41004|1|95" lo pagina.
                    bool esListado = accion == CallbackGasolinerasPagina;
                    var tipoElegido = TipoCarburanteExtensions.DesdeToken(esListado ? valor3 : valor);
                    if (tipoElegido is not { } tipo)
                    {
                        await ReemplazarMensaje(chatId, messageId, PreguntaCarburante.Texto, PreguntaCarburante.Teclado, cancellationToken);
                        break;
                    }

                    int pagina = esListado && int.TryParse(valor2, out int gl) ? gl : 0;
                    var gasolineras = await ConstruirTecladoGasolinerasAsync(pagina, tipo, cancellationToken);
                    await ReemplazarMensaje(chatId, messageId, gasolineras.Texto, gasolineras.Teclado, cancellationToken);
                    break;

                case CallbackElegirCarburante:
                    await ReemplazarMensaje(chatId, messageId, PreguntaCarburante.Texto, PreguntaCarburante.Teclado, cancellationToken);
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

        internal async Task<string> ObtenerTiempoAsync(CancellationToken cancellationToken)
        {
            var clima = await _weatherService.ObtenerTiempoPorMunicipioAsync(
                MunicipioAlcala.CodigoProvincia, MunicipioAlcala.CodigoIne, cancellationToken);

            if (clima?.Municipio is null || clima.Temperaturas is null || clima.EstadoCielo is null)
                return "⚠️ No se pudo obtener la información del clima en este momento.";

            return $"☁️ **El tiempo en {clima.Municipio.Nombre}** ({clima.Municipio.NombreProvincia})\n\n" +
                   $"• Estado: {clima.EstadoCielo.Descripcion}\n" +
                   $"• Actual: {clima.TemperaturaActual}°C | Mín: {clima.Temperaturas.Minima}°C | Máx: {clima.Temperaturas.Maxima}°C\n" +
                   $"• Humedad: {clima.Humedad}% | Viento: {clima.Viento} km/h\n" +
                   $"• Precipitación: {clima.Precipitacion} mm\n" +
                   $"• Actualizado: {clima.Elaborado}";
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
            sb.AppendLine("**Hoy por horas**");
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

                    var partes = new List<string> { $"{hora}h {cielo}" };
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
                sb.AppendLine("**Próximos días**");
                foreach (var dia in dias)
                    sb.AppendLine($"• {EtiquetaDia(dia)}");
            }

            if (!string.IsNullOrWhiteSpace(prediccion.Elaborado))
                sb.AppendLine($"\nActualizado: {Escapar(prediccion.Elaborado)}");

            return sb.ToString();
        }

        private static string EtiquetaDia(DiaPrediccion dia)
        {
            var partes = new List<string> { $"{FormatoDia(dia.Fecha)}" };

            if (dia.Maxima is { } maxima && dia.Minima is { } minima)
                partes.Add($"{Formato(minima, 0)}° / {Formato(maxima, 0)}°C");
            else if (dia.Maxima is { } soloMaxima)
                partes.Add($"máx. {Formato(soloMaxima, 0)}°C");

            if (!string.IsNullOrWhiteSpace(dia.Cielo))
                partes.Add(Escapar(dia.Cielo));
            if (dia.ProbPrecipitacion is { } probabilidad)
                partes.Add($"lluvia {Formato(probabilidad, 0)}%");
            if (dia.Viento is { } viento)
                partes.Add($"viento {Formato(viento, 0)} km/h");
            if (dia.RachaMax is { } racha)
                partes.Add($"racha {Formato(racha, 0)} km/h");
            if (dia.UvMax is { } uv)
                partes.Add($"UV {uv}");

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
        /// Pregunta con qué carburante comparar antes de mostrar las gasolineras. El tipo se
        /// elige una vez y a partir de ahí viaja en el callbackData de cada botón.
        /// </summary>
        internal static (string Texto, InlineKeyboardMarkup Teclado) PreguntaCarburante { get; } = (
            "⛽ **¿Qué carburante quieres consultar?**\n\n" +
            $"Te paso las gasolineras de {MunicipioAlcala.Nombre} ordenadas de más barata a más cara " +
            $"(radio {RadioGasolinerasKm:0} km).",
            new InlineKeyboardMarkup(
            [
                [new InlineKeyboardButton(TipoCarburante.Gasolina95.Boton()) { CallbackData = $"{CallbackGasolineras}|{TipoCarburante.Gasolina95.Token()}" }],
                [new InlineKeyboardButton(TipoCarburante.GasoleoA.Boton()) { CallbackData = $"{CallbackGasolineras}|{TipoCarburante.GasoleoA.Token()}" }],
                ConstruirFilaPrincipal()
            ]));

        internal static InlineKeyboardMarkup TecladoInicio() =>
            new(ConstruirFilaPrincipal());

        internal async Task<(string Texto, InlineKeyboardMarkup Teclado)> ConstruirTecladoGasolinerasAsync(
            int pagina,
            TipoCarburante carburante,
            CancellationToken cancellationToken)
        {
            var gasolineras = await _gasolinaService.ObtenerGasolinerasCercaAsync(
                MunicipioAlcala.CodigoProvincia,
                MunicipioAlcala.Latitud,
                MunicipioAlcala.Longitud,
                RadioGasolinerasKm,
                carburante,
                cancellationToken);

            if (gasolineras.Count == 0)
                return ($"⛽ No hay gasolineras con precio de {carburante.Nombre()} a menos de {RadioGasolinerasKm:0} km de {MunicipioAlcala.Nombre}.",
                        TecladoAviso(BotonCambiarCarburante, CallbackElegirCarburante));

            int totalPaginas = (int)Math.Ceiling(gasolineras.Count / (double)GasolinerasPorPagina);
            pagina = Math.Clamp(pagina, 0, totalPaginas - 1);

            var sb = new StringBuilder();
            string tituloCarburante = carburante == TipoCarburante.GasoleoA ? "Diesel" : carburante.Nombre();
            sb.AppendLine($"⛽ **Gasolineras {MunicipioAlcala.Nombre}**");
            sb.AppendLine($"{tituloCarburante} · radio {RadioGasolinerasKm:0} km · {gasolineras.Count} gasolineras");

            if (totalPaginas > 1)
                sb.AppendLine($"Página {pagina + 1} de {totalPaginas}");

            var paginaActual = gasolineras
                .Skip(pagina * GasolinerasPorPagina)
                .Take(GasolinerasPorPagina);

            int posicion = pagina * GasolinerasPorPagina;
            foreach (var gasolinera in paginaActual)
            {
                posicion++;
                sb.AppendLine($"{posicion}. **{Escapar(gasolinera.Nombre)}** — **{Formato(gasolinera.Precio, 3)} €/L**");
                sb.AppendLine($"   {Escapar(gasolinera.Direccion)}");
            }

            var filas = new List<InlineKeyboardButton[]>();
            if (totalPaginas > 1)
            {
                filas.Add(ConstruirFilaNavegacion(
                    p => $"{CallbackGasolinerasPagina}|{MunicipioAlcala.CodigoIne}|{p}|{carburante.Token()}",
                    pagina,
                    totalPaginas));
            }

            // Solo se ofrece Alcalá de Guadaíra, así que no tiene sentido un botón para
            // cambiar de municipio: queda el de volver a pedir otro carburante.
            filas.Add([
                new InlineKeyboardButton(BotonCambiarCarburante) { CallbackData = CallbackElegirCarburante },
                new InlineKeyboardButton("🗺️ Volver al clima") { CallbackData = CallbackClima }
            ]);
            filas.Add(ConstruirFilaPrincipal());

            return (sb.ToString(), new InlineKeyboardMarkup(filas));
        }

        internal static string Formato(double valor, int decimales) => valor.ToString("F" + decimales, CultureEspanol);

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