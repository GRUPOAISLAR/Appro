using System.Text.Json;
using Bravo.Video.Contratos;
using Bravo.Video.Ingesta.Datos;
using Bravo.Video.Patentes;
using Microsoft.EntityFrameworkCore;

namespace Bravo.Video.Ingesta.Api;

public sealed record EventoRechazado(int Indice, Guid? EventId, IReadOnlyList<string> Errores);

public sealed record RespuestaLote(
    DateTimeOffset HoraServidor,
    int Aceptados,
    int Duplicados,
    IReadOnlyList<EventoRechazado> Rechazados);

/// <summary>
/// Procesa un lote del conector.
///
/// Cada evento se evalúa por separado: uno mal formado se rechaza con su motivo
/// y el resto del lote entra igual. Un rechazo no se arregla reenviando, así que
/// el conector lo saca de la cola y lo cuenta.
///
/// Es idempotente por <c>eventId</c>: lo que ya está guardado se cuenta como
/// duplicado y no se toca, aunque el contenido difiera (el id identifica el
/// hecho; si cambió el contenido, el error está en el conector).
/// </summary>
public sealed class ServicioIngesta(VideoDbContext db, HashPatente hash, TimeProvider reloj, ILogger<ServicioIngesta> log)
{
    public static readonly TimeSpan ToleranciaFuturo = TimeSpan.FromDays(1);

    public async Task<RespuestaLote> ProcesarAsync(Equipo equipo, JsonElement eventos, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var rechazados = new List<EventoRechazado>();
        var candidatos = new List<EventoVideo>();
        var vistos = new HashSet<Guid>();
        var duplicados = 0;
        var indicePorId = new Dictionary<Guid, int>();

        var indice = -1;
        foreach (var elemento in eventos.EnumerateArray())
        {
            indice++;
            EventoVideo? evento;
            try
            {
                evento = elemento.Deserialize<EventoVideo>(OpcionesJson.Eventos);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
            {
                // No se devuelve el mensaje de la excepción, que puede citar contenido
                // del evento. La excepción de fecha es la única con texto fijo propio.
                var motivo = e switch
                {
                    FechaInvalidaException f => f.Message,
                    JsonException { Path: { } path } => $"No respeta el contrato v1 (en {path}).",
                    _ => "No respeta el contrato v1.",
                };
                rechazados.Add(new EventoRechazado(indice, IdDe(elemento), [motivo]));
                continue;
            }

            if (evento is null)
            {
                rechazados.Add(new EventoRechazado(indice, null, ["Evento nulo."]));
                continue;
            }

            var errores = ValidadorEvento.Validar(evento).ToList();
            if (evento.Dispositivo != equipo.Dispositivo)
            {
                errores.Add("dispositivo no coincide con el token.");
            }

            if (evento.Sede != equipo.Sede)
            {
                errores.Add("sede no coincide con la del equipo.");
            }

            // Un reloj adelantado no puede estirar la retención de la patente ni
            // ensuciar los tableros con eventos "del futuro".
            if (evento.Ts.UtcDateTime > ahora.UtcDateTime + ToleranciaFuturo)
            {
                errores.Add("ts está más de un día en el futuro respecto del servidor.");
            }

            if (errores.Count > 0)
            {
                rechazados.Add(new EventoRechazado(indice, evento.EventId, errores));
                continue;
            }

            if (!vistos.Add(evento.EventId))
            {
                duplicados++;
                continue;
            }

            candidatos.Add(evento);
            indicePorId[evento.EventId] = indice;
        }

        var (aceptados, fallidos) = await GuardarAsync(equipo.Dispositivo, candidatos, ahora.UtcDateTime, ct);
        rechazados.AddRange(fallidos.Select(id => new EventoRechazado(
            indicePorId[id], id, ["La base no pudo guardar el evento."])));
        rechazados.Sort((a, b) => a.Indice.CompareTo(b.Indice));
        duplicados += candidatos.Count - aceptados - fallidos.Count;

        equipo.UltimoContactoUtc = ahora.UtcDateTime;
        await db.SaveChangesAsync(ct);

        // Solo cantidades: nada de patentes ni contenido en los logs.
        log.LogInformation(
            "Lote de {Dispositivo}: {Aceptados} aceptados, {Duplicados} duplicados, {Rechazados} rechazados.",
            equipo.Dispositivo, aceptados, duplicados, rechazados.Count);

        return new RespuestaLote(ahora, aceptados, duplicados, rechazados);
    }

    /// <summary>
    /// Guarda los que no existen todavía para este equipo. Si el guardado en bloque
    /// falla dos veces, guarda de a uno: el evento que la base no acepta se rechaza
    /// con motivo y no traba al resto (si no, el conector reintentaría el mismo
    /// lote para siempre).
    /// </summary>
    private async Task<(int Aceptados, List<Guid> Fallidos)> GuardarAsync(
        string dispositivo, List<EventoVideo> candidatos, DateTime recibidoUtc, CancellationToken ct)
    {
        if (candidatos.Count == 0)
        {
            return (0, []);
        }

        // Primer intento en bloque; el segundo cubre que otro pedido del mismo equipo
        // haya guardado alguno entre la consulta y el guardado.
        for (var intento = 1; intento <= 2; intento++)
        {
            var nuevos = await NuevosAsync(dispositivo, candidatos, ct);
            db.Eventos.AddRange(nuevos.Select(e => Mapeo.AGuardado(e, recibidoUtc, hash)));
            try
            {
                await db.SaveChangesAsync(ct);
                return (nuevos.Count, []);
            }
            catch (DbUpdateException)
            {
                Soltar();
            }
        }

        var aceptados = 0;
        var fallidos = new List<Guid>();
        foreach (var evento in await NuevosAsync(dispositivo, candidatos, ct))
        {
            db.Eventos.Add(Mapeo.AGuardado(evento, recibidoUtc, hash));
            try
            {
                await db.SaveChangesAsync(ct);
                aceptados++;
            }
            catch (DbUpdateException e)
            {
                Soltar();
                fallidos.Add(evento.EventId);
                log.LogError("La base rechazó un evento ({Tipo}).", e.InnerException?.GetType().Name ?? e.GetType().Name);
            }
        }

        return (aceptados, fallidos);
    }

    private async Task<List<EventoVideo>> NuevosAsync(string dispositivo, List<EventoVideo> candidatos, CancellationToken ct)
    {
        var ids = candidatos.Select(e => e.EventId).ToList();
        var existentes = await db.Eventos.AsNoTracking()
            .Where(e => e.Dispositivo == dispositivo && ids.Contains(e.EventId))
            .Select(e => e.EventId)
            .ToHashSetAsync(ct);
        return candidatos.Where(e => !existentes.Contains(e.EventId)).ToList();
    }

    private void Soltar()
    {
        foreach (var entrada in db.ChangeTracker.Entries<EventoGuardado>().ToList())
        {
            entrada.State = EntityState.Detached;
        }
    }

    private static Guid? IdDe(JsonElement elemento) =>
        elemento.ValueKind == JsonValueKind.Object
        && elemento.TryGetProperty("eventId", out var id)
        && id.ValueKind == JsonValueKind.String
        && Guid.TryParse(id.GetString(), out var guid)
            ? guid
            : null;
}
