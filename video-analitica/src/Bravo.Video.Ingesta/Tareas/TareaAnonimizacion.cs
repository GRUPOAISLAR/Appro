using Bravo.Video.Contratos;
using Bravo.Video.Ingesta.Datos;
using Microsoft.EntityFrameworkCore;

namespace Bravo.Video.Ingesta.Tareas;

/// <summary>
/// Borra la patente en texto (leída y normalizada) y el id de origen de los
/// eventos de vehículo más viejos que el plazo de retención, y deja solo el hash.
/// El plazo se cuenta desde lo que haya pasado primero: la hora del hecho o la
/// llegada al servidor. Así un equipo con el reloj adelantado no estira la
/// retención.
///
/// Es un UPDATE por lote, idempotente: si el servidor se cae a la mitad, la
/// próxima corrida sigue donde quedó.
/// </summary>
public sealed class TareaAnonimizacion(
    IServiceScopeFactory alcances,
    ConfiguracionIngesta configuracion,
    TimeProvider reloj,
    ILogger<TareaAnonimizacion> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var temporizador = new PeriodicTimer(configuracion.IntervaloAnonimizacion, reloj);
        do
        {
            try
            {
                await using var alcance = alcances.CreateAsyncScope();
                var db = alcance.ServiceProvider.GetRequiredService<VideoDbContext>();
                var cantidad = await AnonimizarAsync(db, reloj.GetUtcNow().UtcDateTime, configuracion.RetencionPatente, ct);
                if (cantidad > 0)
                {
                    log.LogInformation("Anonimización: {Cantidad} eventos pasaron a solo hash.", cantidad);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Falló la anonimización; se reintenta en la próxima corrida.");
            }
        }
        while (await temporizador.WaitForNextTickAsync(ct));
    }

    public static Task<int> AnonimizarAsync(VideoDbContext db, DateTime ahoraUtc, TimeSpan retencion, CancellationToken ct)
    {
        var corte = ahoraUtc - retencion;
        return db.Eventos
            .Where(e => e.PatenteAnonimizadaUtc == null
                && (e.Tipo == TiposEvento.VehiculoIngreso || e.Tipo == TiposEvento.VehiculoEgreso)
                && (e.TsUtc < corte || e.RecibidoUtc < corte))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.PatenteLeida, (string?)null)
                    .SetProperty(e => e.PatenteNormalizada, (string?)null)
                    // El id de origen lo genera el motor; por defensa se borra también.
                    .SetProperty(e => e.IdOrigen, (string?)null)
                    .SetProperty(e => e.PatenteAnonimizadaUtc, ahoraUtc),
                ct);
    }
}
