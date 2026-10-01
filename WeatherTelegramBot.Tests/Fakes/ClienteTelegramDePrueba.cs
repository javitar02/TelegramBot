using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace WeatherTelegramBot.Tests.Fakes;

/// <summary>
/// Doble de <see cref="ITelegramBotClient"/>.
///
/// NSubstitute no sirve aquí: SendMessage, EditMessageText y AnswerCallbackQuery no son
/// miembros de la interfaz, sino métodos de extensión de TelegramBotClientExtensions, y solo
/// SendRequest es interceptable. Implementando SendRequest se registra la petición ya
/// construida por la librería, de modo que el código de producción se ejercita de verdad.
/// </summary>
internal sealed class ClienteTelegramDePrueba : ITelegramBotClient
{
    public List<PeticionTelegram> Peticiones { get; } = [];

    /// <summary>Si se asigna, la siguiente petición falla con esta excepción.</summary>
    public Exception? ErrorAlResponder { get; set; }

    public bool LocalBotServer { get; set; }
    public long BotId => 1;
    public TimeSpan Timeout { get; set; }
    public IExceptionParser ExceptionsParser { get; set; } = null!;

#pragma warning disable CS0067
    public event AsyncEventHandler<ApiRequestEventArgs>? OnMakingApiRequest;
    public event AsyncEventHandler<ApiResponseEventArgs>? OnApiResponseReceived;
#pragma warning restore CS0067

    /// <summary>Si se asigna, solo fallan las peticiones con ese MethodName.</summary>
    public (string Metodo, Exception Excepcion)? ErrorEnMetodo { get; set; }

    public Task<TResponse> SendRequest<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        var peticion = new PeticionTelegram(request);
        Peticiones.Add(peticion);

        if (ErrorEnMetodo is { } fallo && fallo.Metodo == peticion.Metodo)
            return Task.FromException<TResponse>(fallo.Excepcion);

        if (ErrorAlResponder is not null)
            return Task.FromException<TResponse>(ErrorAlResponder);

        return Task.FromResult((TResponse)Activator.CreateInstance(typeof(TResponse))!);
    }

    public Task<bool> TestApi(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task DownloadFile(string filePath, Stream destination, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DownloadFile(TGFile file, Stream destination, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public PeticionTelegram UltimaPeticion => Peticiones[^1];

    public IEnumerable<PeticionTelegram> DeMetodo(string metodo) =>
        Peticiones.Where(p => p.Metodo == metodo);
}

internal sealed record PeticionTelegram(IRequest Peticion)
{
    public string Metodo => Peticion.MethodName;

    public string? Texto => Peticion switch
    {
        SendMessageRequest r => r.Text,
        EditMessageTextRequest r => r.Text,
        _ => null
    };

    public long? ChatId => Peticion switch
    {
        SendMessageRequest r => r.ChatId.Identifier,
        EditMessageTextRequest r => r.ChatId.Identifier,
        _ => null
    };

    public int? MessageId => Peticion is EditMessageTextRequest r ? r.MessageId : null;

    public string? CallbackQueryId =>
        Peticion is AnswerCallbackQueryRequest r ? r.CallbackQueryId : null;

    public ParseMode? ModoParseo => Peticion switch
    {
        SendMessageRequest r => r.ParseMode,
        EditMessageTextRequest r => r.ParseMode,
        _ => null
    };

    public ReplyMarkup? Teclado => Peticion switch
    {
        SendMessageRequest r => r.ReplyMarkup,
        EditMessageTextRequest r => r.ReplyMarkup,
        _ => null
    };

    public IReadOnlyList<string?> Callbacks =>
        Teclado is InlineKeyboardMarkup inline
            ? inline.InlineKeyboard.SelectMany(f => f).Select(b => b.CallbackData).ToList()
            : [];

    public IReadOnlyList<string> Botones =>
        Teclado is ReplyKeyboardMarkup teclado
            ? teclado.Keyboard.SelectMany(f => f).Select(b => b.Text).ToList()
            : [];
}
