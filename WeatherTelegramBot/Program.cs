using Telegram.Bot;
using WeatherTelegramBot.Services;

var builder = Host.CreateApplicationBuilder(args);

const string UserAgent = "TelegramBot-DotNet/1.0";

// Catálogo de municipios y previsión: se cachean en memoria, así que se registran como
// singleton. AddHttpClient<T> crearía el servicio en cada request y tiraría la caché.
builder.Services.AddHttpClient(ElTiempoApi.NombreCliente, client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("miteco", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<IMunicipioService, MunicipioService>();
builder.Services.AddSingleton<IWeatherService, WeatherService>();
builder.Services.AddSingleton<IGasolinaService, GasolinaService>();

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var token = config["Telegram:TelegramBotToken"];

    if (string.IsNullOrWhiteSpace(token))
        throw new InvalidOperationException(
            "Falta configurar Telegram:TelegramBotToken. Usa User Secrets: "
            + "dotnet user-secrets set \"Telegram:TelegramBotToken\" \"<token>\"");

    return new TelegramBotClient(token);
});

builder.Services.AddHostedService<TelegramBotService>();

var host = builder.Build();
await host.RunAsync();
