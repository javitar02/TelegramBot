using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WeatherTelegramBot.Services;

namespace WeatherTelegramBot.Tests.Fakes;

/// <summary>
/// Harness del bot con el cliente de Telegram grabado y los tres servicios sustituidos por
/// NSubstitute, para poder afirmar tanto el mensaje saliente como los argumentos con los que
/// se llamó a cada servicio.
/// </summary>
internal sealed class BotDePrueba
{
    public ClienteTelegramDePrueba Cliente { get; } = new();

    public IWeatherService Clima { get; } = Substitute.For<IWeatherService>();
    public IGasolinaService Gasolina { get; } = Substitute.For<IGasolinaService>();

    public IPrediccionService Prediccion { get; } = Substitute.For<IPrediccionService>();

    public TelegramBotService Bot { get; }

    public BotDePrueba()
    {
        Bot = new TelegramBotService(Cliente, Clima, Gasolina, Prediccion, LoggerFactory());
    }

    public static Microsoft.Extensions.Logging.ILogger<TelegramBotService> LoggerFactory() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<TelegramBotService>.Instance;

    public static Update Mensaje(long chatId, string? texto) => new()
    {
        Id = 1,
        Message = new Message
        {
            Id = 500,
            Chat = new Chat { Id = chatId, Type = ChatType.Private },
            Text = texto
        }
    };

    public static Update SinMensajeDeTexto(long chatId) => new()
    {
        Id = 2,
        Message = new Message { Id = 501, Chat = new Chat { Id = chatId, Type = ChatType.Private } }
    };

    public static Update Callback(long chatId, string? datos, int messageId = 500) => new()
    {
        Id = 3,
        CallbackQuery = new CallbackQuery
        {
            Id = "cb-1",
            Data = datos,
            Message = new Message { Id = messageId, Chat = new Chat { Id = chatId, Type = ChatType.Private } }
        }
    };

    public static CallbackQuery CallbackQuery(long chatId, string? datos, int messageId = 500) =>
        new()
        {
            Id = "cb-1",
            Data = datos,
            Message = new Message { Id = messageId, Chat = new Chat { Id = chatId, Type = ChatType.Private } }
        };

    public static CallbackQuery CallbackQuerySinMensaje(string? datos) => new() { Id = "cb-1", Data = datos };

    public PeticionTelegram Ultima => Cliente.UltimaPeticion;

    public PeticionTelegram UltimoEdit => Cliente.DeMetodo("editMessageText").Last();

    public PeticionTelegram UltimoSend => Cliente.DeMetodo("sendMessage").Last();

    public IReadOnlyList<string?> Callbacks => Cliente.UltimaPeticion.Callbacks;

    public PeticionTelegram ConCallback(string esperado) =>
        Cliente.Peticiones.Single(p => p.Callbacks.Contains(esperado));
}
