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
        private const int MunicipiosPorPagina = 20;
        private const int GasolinerasPorPagina = 15;
        private const int Columnas = 2;
        private const double RadioGasolinerasKm = 10;
        private const double DepositoLitros = 50;

        private const string BotonClima = "☀️ Consultar Clima";
        private const string BotonGasofa = "⛽ Consultar Gasofa";
        private const string BotonMenu = "🏠 Menú principal";
        private const string BotonOtroMunicipio = "🔙 Otro municipio";
        private const string BotonCambiarCarburante = "🔄 Cambiar carburante";

        private const string CallbackMenu = "menu";
        private const string PrefijoClima = "w";
        private const string PrefijoGasofa = "g";
        private const string CallbackIrClima = "ir_clima";
        private const string CallbackIrGasofa = "ir_gasofa";

        /// <summary>Pregunta con qué carburante comparar, antes de elegir municipio.</summary>
        private const string CallbackElegirCarburante = "tipo";

        /// <summary>Municipios de la provincia ya con el carburante elegido ("ga|95").</summary>
        private const string CallbackMunicipiosGasofa = "ga";

        private const string PrefijoGasofaPagina = "gp";
        private const string PrefijoGasofaSalto = "gj";
        private const string PrefijoGasofaListado = "gl";

        // En los custom format de .NET la coma es separador de millares, no decimal:
        // hay que pedir la cultura española y usar F1/F2/F3 para obtener "3,4" y "1,799".
        private static readonly CultureInfo CultureEspanol = CultureInfo.GetCultureInfo("es-ES");

        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<TelegramBotService> _logger;
        private readonly IWeatherService _weatherService;
        private readonly IMunicipioService _municipioService;
        private readonly IGasolinaService _gasolinaService;

        public TelegramBotService(
            ITelegramBotClient botClient,
            IWeatherService weatherService,
            IMunicipioService municipioService,
            IGasolinaService gasolinaService,
            ILogger<TelegramBotService> logger)
        {
            _botClient = botClient;
            _weatherService = weatherService;
            _municipioService = municipioService;
            _gasolinaService = gasolinaService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            _logger.LogInformation("Iniciando Bot de Telegram...");

            try
            {
                var municipios = await _municipioService.ObtenerMunicipiosAsync(stoppingToken);
                _logger.LogInformation("Catálogo de municipios listo ({Cantidad} municipios)", municipios.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo precargar el catálogo de municipios");
            }

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
                    await botClient.SendMessage(
                        chatId: chatId,
                        text: $"¡Hola! Soy tu bot del tiempo 👋\n\n" +
                              "Elige qué quieres consultar y te muestro los municipios de Sevilla.",
                        parseMode: ParseMode.Markdown,
                        replyMarkup: TecladoInicio(),
                        cancellationToken: cancellationToken
                    );
                    return;
                }

                string? respuesta = texto switch
                {
                    BotonClima or BotonGasofa => null,
                    _ => "No te he entendido. Usa los botones del menú para consultar el tiempo 🌤️ o los precios de la gasolina ⛽"
                };

                if (respuesta is null)
                {
                    if (texto == BotonGasofa)
                    {
                        await EnviarPreguntaCarburante(botClient, chatId, cancellationToken);
                        return;
                    }

                    var menu = await ConstruirTecladoMunicipiosAsync(0, null, cancellationToken);
                    await botClient.SendMessage(
                        chatId: chatId,
                        text: menu.Texto,
                        parseMode: ParseMode.Markdown,
                        replyMarkup: menu.Teclado,
                        cancellationToken: cancellationToken
                    );
                    return;
                }

                await botClient.SendMessage(
                    chatId: chatId,
                    text: respuesta,
                    cancellationToken: cancellationToken
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar la actualización {UpdateId}", update.Id);
            }
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
                case "pg":
                case "jp":
                    int paginaClima = accion switch
                    {
                        "pg" when int.TryParse(valor, out int p) => p,
                        "jp" => await ResolverPaginaInicialAsync(valor, cancellationToken),
                        _ => 0
                    };

                    var menuClima = await ConstruirTecladoMunicipiosAsync(paginaClima, null, cancellationToken);
                    await ReemplazarMensaje(chatId, messageId, menuClima.Texto, menuClima.Teclado, cancellationToken);
                    break;

                case PrefijoGasofaPagina:
                case PrefijoGasofaSalto:
                case CallbackMunicipiosGasofa:
                    // En todo el recorrido de gasolineras el tipo de carburante viaja en el
                    // propio callback: el bot es stateless y no puede guardarlo por chat.
                    if (TipoCarburanteExtensions.DesdeToken(valor2) is not { } tipoElegido)
                    {
                        await ReemplazarMensaje(chatId, messageId, PreguntaCarburante.Texto, PreguntaCarburante.Teclado, cancellationToken);
                        break;
                    }

                    int paginaGasofa = accion switch
                    {
                        PrefijoGasofaPagina when int.TryParse(valor, out int p) => p,
                        PrefijoGasofaSalto => await ResolverPaginaInicialAsync(valor, cancellationToken),
                        _ => 0
                    };

                    var menuGasofa = await ConstruirTecladoMunicipiosAsync(paginaGasofa, tipoElegido, cancellationToken);
                    await ReemplazarMensaje(chatId, messageId, menuGasofa.Texto, menuGasofa.Teclado, cancellationToken);
                    break;

                case CallbackIrClima:
                    var listadoClima = await ConstruirTecladoMunicipiosAsync(0, null, cancellationToken);
                    await ReemplazarMensaje(chatId, messageId, listadoClima.Texto, listadoClima.Teclado, cancellationToken);
                    break;

                case CallbackIrGasofa:
                case CallbackElegirCarburante:
                    await ReemplazarMensaje(chatId, messageId, PreguntaCarburante.Texto, PreguntaCarburante.Teclado, cancellationToken);
                    break;

                case PrefijoClima:
                    await ReemplazarMensaje(chatId, messageId, await ObtenerTiempoPorCodigoAsync(valor, cancellationToken), ConstruirTecladoVolver(), cancellationToken);
                    break;

                case PrefijoGasofa:
                case PrefijoGasofaListado:
                    // "g|<codigo>|<tipo>" arranca el listado y "gl|<codigo>|<pagina>|<tipo>" lo pagina.
                    bool esListado = accion == PrefijoGasofaListado;
                    var tipoListado = TipoCarburanteExtensions.DesdeToken(esListado ? valor3 : valor2) ?? TipoCarburante.Gasolina95;
                    int paginaListado = esListado && int.TryParse(valor2, out int gl) ? gl : 0;

                    var gasolineras = await ConstruirTecladoGasolinerasAsync(valor, paginaListado, tipoListado, cancellationToken);
                    await ReemplazarMensaje(chatId, messageId, gasolineras.Texto, gasolineras.Teclado, cancellationToken);
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

        internal static InlineKeyboardMarkup ConstruirTecladoVolver()
        {
            return new InlineKeyboardMarkup(
            [
                [new InlineKeyboardButton("🗺️ Volver a los municipios") { CallbackData = CallbackMenu }],
                ConstruirFilaPrincipal()
            ]);
        }

        /// <summary>
        /// Fila de acceso directo a los dos menús. Se añade al pie de todos los teclados
        /// para que se pueda cambiar de consulta sin volver al menú principal, y replaces
        /// el reply keyboard: los botones inline no desaparecen al enviar otro mensaje.
        /// </summary>
        private static InlineKeyboardButton[] ConstruirFilaPrincipal() =>
        [
            new(BotonClima) { CallbackData = CallbackIrClima },
            new(BotonGasofa) { CallbackData = CallbackIrGasofa }
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

        internal async Task<string> ObtenerTiempoPorCodigoAsync(string codigo, CancellationToken cancellationToken)
        {
            var clima = await _weatherService.ObtenerTiempoPorMunicipioAsync(
                ElTiempoApi.CodProvinciaSevilla, codigo, cancellationToken);

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
        /// Pregunta con qué carburante comparar antes de elegir municipio. Se muestra siempre
        /// antes de las gasolineras: el tipo se elige una vez y a partir de ahí viaja en el
        /// callbackData de cada botón.
        /// </summary>
        internal static (string Texto, InlineKeyboardMarkup Teclado) PreguntaCarburante { get; } = (
            "⛽ **¿Qué carburante quieres consultar?**\n\n" +
            "Elige el combustible y después el municipio: te paso las gasolineras " +
            $"ordenadas de más barata a más cara (radio {RadioGasolinerasKm:0} km).",
            new InlineKeyboardMarkup(
            [
                [new InlineKeyboardButton(TipoCarburante.Gasolina95.Boton()) { CallbackData = $"{CallbackMunicipiosGasofa}|{TipoCarburante.Gasolina95.Token()}" }],
                [new InlineKeyboardButton(TipoCarburante.GasoleoA.Boton()) { CallbackData = $"{CallbackMunicipiosGasofa}|{TipoCarburante.GasoleoA.Token()}" }],
                ConstruirFilaPrincipal()
            ]));

        private async Task EnviarPreguntaCarburante(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
        {
            await botClient.SendMessage(
                chatId: chatId,
                text: PreguntaCarburante.Texto,
                parseMode: ParseMode.Markdown,
                replyMarkup: PreguntaCarburante.Teclado,
                cancellationToken: cancellationToken
            );
        }

        internal async Task<(string Texto, InlineKeyboardMarkup Teclado)> ConstruirTecladoMunicipiosAsync(
            int pagina,
            TipoCarburante? carburante,
            CancellationToken cancellationToken)
        {
            var municipios = await _municipioService.ObtenerMunicipiosAsync(cancellationToken);

            if (municipios.Count == 0)
                return ("⚠️ No se pudo cargar el catálogo de municipios de Sevilla.", TecladoAviso());

            // Cada modo usa su propio prefijo de callback para que el mismo teclado sirva
            // para el clima (w) y para las gasolineras (g). Además, el recorrido de
            // gasolineras arrastra el token del carburante para no perderlo al paginar.
            bool modoGasofa = carburante is not null;
            string prefijo = modoGasofa ? PrefijoGasofa : PrefijoClima;
            string prefijoPagina = modoGasofa ? PrefijoGasofaPagina : "pg";
            string prefijoSalto = modoGasofa ? PrefijoGasofaSalto : "jp";
            string sufijo = modoGasofa ? $"|{carburante!.Value.Token()}" : "";

            int totalPaginas = (int)Math.Ceiling(municipios.Count / (double)MunicipiosPorPagina);
            pagina = Math.Clamp(pagina, 0, totalPaginas - 1);

            var filas = new List<InlineKeyboardButton[]>();

            var botones = municipios
                .Skip(pagina * MunicipiosPorPagina)
                .Take(MunicipiosPorPagina)
                .Select(m => new InlineKeyboardButton(m.Nombre)
                {
                    CallbackData = $"{prefijo}|{m.CodigoIne.Substring(0, 5)}{sufijo}"
                })
                .ToArray();

            for (int i = 0; i < botones.Length; i += Columnas)
                filas.Add(i + 1 < botones.Length ? [botones[i], botones[i + 1]] : [botones[i]]);

            filas.Add(ConstruirFilaNavegacion(p => $"{prefijoPagina}|{p}{sufijo}", pagina, totalPaginas));

            foreach (var grupo in ConstruirBotonesIniciales(municipios, prefijoSalto, sufijo))
                filas.Add(grupo);

            filas.Add(ConstruirFilaPrincipal());

            string titulo = modoGasofa
                ? $"⛽ **Gasolineras de {carburante!.Value.Nombre()} en Sevilla** — página {pagina + 1} de {totalPaginas}\n\n" +
                  $"Elige tu municipio y te las ordeno de más barata a más cara (radio {RadioGasolinerasKm:0} km):"
                : $"🗺️ **Municipios de Sevilla** — página {pagina + 1} de {totalPaginas}\n\nElige el municipio:";

            return (titulo, new InlineKeyboardMarkup(filas));
        }

        internal static IEnumerable<InlineKeyboardButton[]> ConstruirBotonesIniciales(
            IReadOnlyList<MunicipioCatalogo> municipios,
            string prefijoSalto,
            string sufijo = "")
        {
            var iniciales = municipios
                .Select(m => ObtenerInicial(m.Nombre))
                .Distinct()
                .OrderBy(c => c)
                .Select(c => new InlineKeyboardButton(c.ToString()) { CallbackData = $"{prefijoSalto}|{c}{sufijo}" })
                .ToArray();

            const int BotonesPorFila = 9;
            for (int i = 0; i < iniciales.Length; i += BotonesPorFila)
            {
                yield return i + BotonesPorFila - 1 < iniciales.Length
                    ? iniciales[i..(i + BotonesPorFila)]
                    : iniciales[i..];
            }
        }

        private async Task<int> ResolverPaginaInicialAsync(string inicial, CancellationToken cancellationToken)
        {
            var municipios = await _municipioService.ObtenerMunicipiosAsync(cancellationToken);
            if (string.IsNullOrEmpty(inicial))
                return 0;

            char letra = char.ToUpperInvariant(inicial[0]);

            for (int i = 0; i < municipios.Count; i++)
            {
                if (ObtenerInicial(municipios[i].Nombre) == letra)
                    return i / MunicipiosPorPagina;
            }

            return 0;
        }

        internal static char ObtenerInicial(string nombre)
        {
            return string.IsNullOrWhiteSpace(nombre) ? '#' : char.ToUpperInvariant(nombre.Trim()[0]);
        }

        internal static InlineKeyboardMarkup TecladoInicio() =>
            new(ConstruirFilaPrincipal());

        internal async Task<(string Texto, InlineKeyboardMarkup Teclado)> ConstruirTecladoGasolinerasAsync(
            string codigo,
            int pagina,
            CancellationToken cancellationToken)
        {
            var municipios = await _municipioService.ObtenerMunicipiosAsync(cancellationToken);
            var municipio = municipios.FirstOrDefault(m => m.CodigoIne.StartsWith(codigo, StringComparison.Ordinal));

            if (municipio is null)
                return ("⚠️ No se encontró ese municipio.", TecladoAviso());

            if (municipio.Latitud is null || municipio.Longitud is null)
                return ($"⚠️ No tengo coordenadas de {Escapar(municipio.Nombre)} para buscar gasolineras.", TecladoAviso());

            var resultado = await _gasolinaService.ObtenerGasolinerasCercaAsync(
                municipio.CodProv, municipio.Latitud.Value, municipio.Longitud.Value, RadioGasolinerasKm, cancellationToken);

            if (resultado.Gasolineras.Count == 0)
                return ($"⛽ No hay gasolineras con precio de gasolina 95 a menos de {RadioGasolinerasKm:0} km de {Escapar(municipio.Nombre)}.", TecladoAviso(BotonOtroMunicipio, CallbackVolverMunicipios));

            int totalPaginas = (int)Math.Ceiling(resultado.Gasolineras.Count / (double)GasolinerasPorPagina);
            pagina = Math.Clamp(pagina, 0, totalPaginas - 1);

            var sb = new StringBuilder();
            sb.AppendLine($"⛽ **Gasolineras cerca de {Escapar(municipio.Nombre)}**");
            sb.AppendLine($"Gasolina 95 E5 · radio {RadioGasolinerasKm:0} km · {resultado.Gasolineras.Count} gasolineras");

            if (resultado.Actualizado is not null)
                sb.AppendLine($"Datos MITECO: {Escapar(resultado.Actualizado)}");
            if (totalPaginas > 1)
                sb.AppendLine($"Página {pagina + 1} de {totalPaginas}");

            var paginaActual = resultado.Gasolineras
                .Skip(pagina * GasolinerasPorPagina)
                .Take(GasolinerasPorPagina);

            int posicion = pagina * GasolinerasPorPagina;
            foreach (var gasolinera in paginaActual)
            {
                posicion++;
                sb.AppendLine($"{posicion}. {Escapar(gasolinera.Nombre)} — **{Formato(gasolinera.PrecioGasolina95, 3)} €/L** · {Formato(gasolinera.DistanciaKm, 1)} km");
            }

            var masBarata = resultado.Gasolineras.First();
            var masCara = resultado.Gasolineras.Last();
            double ahorro = (masCara.PrecioGasolina95 - masBarata.PrecioGasolina95) * DepositoLitros;
            sb.AppendLine();
            sb.AppendLine($"💰 De {Formato(masBarata.PrecioGasolina95, 3)} a {Formato(masCara.PrecioGasolina95, 3)} €/L: hasta **{Formato(ahorro, 2)} €** de diferencia en un depósito de {DepositoLitros:0} L.");

            var filas = new List<InlineKeyboardButton[]>();
            if (totalPaginas > 1)
                filas.Add(ConstruirFilaNavegacion(p => $"gl|{codigo}|{p}", pagina, totalPaginas));

            filas.Add([new InlineKeyboardButton(BotonOtroMunicipio) { CallbackData = CallbackVolverMunicipios }]);
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