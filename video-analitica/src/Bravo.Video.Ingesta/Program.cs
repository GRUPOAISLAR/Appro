using System.Text.Json;
using Bravo.Video.Contratos;
using Bravo.Video.Ingesta;
using Bravo.Video.Ingesta.Api;
using Bravo.Video.Ingesta.Datos;
using Bravo.Video.Ingesta.Equipos;
using Bravo.Video.Ingesta.Tareas;
using Bravo.Video.Patentes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

if (args.Length > 0 && Comandos.Existe(args[0]))
{
    return await Comandos.EjecutarAsync(args);
}

var builder = WebApplication.CreateBuilder(args);
var configuracion = ConfiguracionIngesta.Leer(builder.Configuration);

builder.Services.AddSingleton(configuracion);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new HashPatente(configuracion.ClaveHashPatente));
builder.Services.AddDbContext<VideoDbContext>(o => Base.Configurar(o, configuracion));
builder.Services.AddScoped<ServicioIngesta>();
builder.Services.AddHostedService<TareaAnonimizacion>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new ConvertidorFechaConZona());
});

var app = builder.Build();

app.MapGet("/salud", () => Results.Ok(new { estado = "ok" }));

app.MapPost("/api/v1/eventos", async (HttpRequest pedido, VideoDbContext db, ServicioIngesta servicio, CancellationToken ct) =>
    {
        var equipo = await Autenticacion.EquipoAsync(pedido, db, ct);
        if (equipo is null)
        {
            return Results.Unauthorized();
        }

        JsonDocument cuerpo;
        try
        {
            cuerpo = await JsonDocument.ParseAsync(pedido.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return Results.Problem("El cuerpo no es JSON válido.", statusCode: StatusCodes.Status400BadRequest);
        }

        using (cuerpo)
        {
            if (cuerpo.RootElement.ValueKind != JsonValueKind.Object
                || !cuerpo.RootElement.TryGetProperty("eventos", out var eventos)
                || eventos.ValueKind != JsonValueKind.Array)
            {
                return Results.Problem("Se espera {\"eventos\": [...]}.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (eventos.GetArrayLength() > ConfiguracionIngesta.MaximoEventosPorLote)
            {
                return Results.Problem(
                    $"Máximo {ConfiguracionIngesta.MaximoEventosPorLote} eventos por lote.",
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            return Results.Ok(await servicio.ProcesarAsync(equipo, eventos, ct));
        }
    })
    // 500 eventos de ~600 bytes entran holgados en 2 MB.
    .WithMetadata(new RequestSizeLimitAttribute(2 * 1024 * 1024));

app.Run();
return 0;

/// <summary>Visible para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
