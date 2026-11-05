using Bravo.Video.Conector;
using Bravo.Video.Conector.Cola;
using Bravo.Video.Conector.Envio;
using Bravo.Video.Conector.Adaptadores;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var configuracion = ConfiguracionConector.Leer(builder.Configuration);

// Sin internet, el cliente HTTP de .NET loguea cada reintento con el stack trace
// completo. El enviador ya deja un aviso de una línea; esto evita llenar el disco del local.
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Error);

builder.Services.AddSingleton(configuracion);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<EstadoConector>();
builder.Services.AddSingleton(new ColaDisco(configuracion.DirectorioCola, configuracion.MaximoCola));
builder.Services.AddSingleton<ProcesadorFrigate>();
builder.Services.AddSingleton<ReceptorHttp>();
builder.Services.AddHttpClient<EnviadorCola>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHostedService(s => s.GetRequiredService<EnviadorCola>());
builder.Services.AddHostedService<ClienteMqttFrigate>();
builder.Services.AddHostedService<Latido>();

var app = builder.Build();

app.MapGet("/salud", (ColaDisco cola, EstadoConector estado) => Results.Ok(new
{
    colaPendiente = cola.Pendientes,
    enCuarentena = cola.EnCuarentena,
    ilegibles = cola.Ilegibles,
    mqttConectado = estado.MqttConectado,
    desvioRelojMs = estado.DesvioRelojMs,
}));

// Una alerta ANPR con dos o tres imágenes ronda 1 MB; 5 MB deja margen.
app.MapPost("/anpr/{camara}", (string camara, HttpRequest pedido, ReceptorHttp receptor, CancellationToken ct) =>
        receptor.AnprAsync(camara, pedido, ct))
    .WithMetadata(new RequestSizeLimitAttribute(5 * 1024 * 1024));

app.MapPost("/conteo/{sensor}", (string sensor, HttpRequest pedido, ReceptorHttp receptor, CancellationToken ct) =>
        receptor.ConteoAsync(sensor, pedido, ct))
    .WithMetadata(new RequestSizeLimitAttribute(256 * 1024));

app.Run();

/// <summary>Visible para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
