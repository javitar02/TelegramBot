using System.Net;
using System.Text;

namespace WeatherTelegramBot.Tests.Fakes;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        Responder = responder;
    }

    /// <summary>Se puede cambiar entre llamadas para simular outages que se recuperan.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }

    public List<string> Urls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Urls.Add(request.RequestUri?.ToString() ?? string.Empty);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Responder(request));
    }

    public static FakeHttpMessageHandler Json(string cuerpo) =>
        new(_ => RespuestasHttp.Ok(cuerpo));

    public static FakeHttpMessageHandler Estado(HttpStatusCode codigo, string cuerpo = "") =>
        new(_ => RespuestasHttp.Estado(codigo, cuerpo));

    public static FakeHttpMessageHandler Falla(Exception excepcion) =>
        new(_ => throw excepcion);
}

internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    internal const string ClienteGasolina = "miteco";
    internal const string ClienteElTiempo = "eltiempo";

    private readonly Dictionary<string, FakeHttpMessageHandler> _handlers = [];

    public FakeHttpClientFactory Registrar(string nombreCliente, FakeHttpMessageHandler handler)
    {
        _handlers[nombreCliente] = handler;
        return this;
    }

    public FakeHttpClientFactory RegistrarGasolina(FakeHttpMessageHandler handler) =>
        Registrar(ClienteGasolina, handler);

    public FakeHttpClientFactory RegistrarElTiempo(FakeHttpMessageHandler handler) =>
        Registrar(ClienteElTiempo, handler);

    /// <summary>Nº de peticiones que ha recibido el cliente indicado.</summary>
    public int Peticiones(string nombreCliente) => _handlers[nombreCliente].Urls.Count;

    public IReadOnlyList<string> Urls(string nombreCliente) => _handlers[nombreCliente].Urls;

    public HttpClient CreateClient(string name)
    {
        if (!_handlers.TryGetValue(name, out var handler))
            throw new InvalidOperationException($"No hay handler registrado para el cliente '{name}'.");

        return new HttpClient(handler, disposeHandler: false);
    }
}

internal static class RespuestasHttp
{
    public static HttpResponseMessage Ok(string cuerpo) => Crear(HttpStatusCode.OK, cuerpo);

    public static HttpResponseMessage Estado(HttpStatusCode codigo, string cuerpo = "") => Crear(codigo, cuerpo);

    private static HttpResponseMessage Crear(HttpStatusCode codigo, string cuerpo) =>
        new(codigo) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
}
