using Bravo.Video.Contratos;

namespace Bravo.Video.Estadias;

/// <summary>Parámetros de la regla. Se guardan junto al resultado para poder reproducirlo.</summary>
public sealed record ReglaEstadias
{
    /// <summary>
    /// Estadía más larga que se acepta. Un egreso más alejado que esto de su
    /// ingreso no se empareja: es más probable una lectura perdida que un auto
    /// estacionado tanto tiempo.
    /// </summary>
    public required TimeSpan DuracionMaxima { get; init; }
}

public sealed record Estadia
{
    public required string Sede { get; init; }
    public required string Pais { get; init; }
    public required string Patente { get; init; }

    /// <summary>Eventos que forman la estadía: con estos dos ids se rehace el cálculo.</summary>
    public required Guid EventoIngreso { get; init; }

    public required Guid EventoEgreso { get; init; }
    public required DateTimeOffset Ingreso { get; init; }
    public required DateTimeOffset Egreso { get; init; }
    public TimeSpan Duracion => Egreso - Ingreso;
}

public enum MotivoIngresoSinPareja
{
    /// <summary>Todavía puede llegar el egreso: no pasó la duración máxima hasta el corte.</summary>
    Abierto,

    /// <summary>Llegó otro ingreso de la misma patente antes que un egreso: se perdió la lectura de salida.</summary>
    ReemplazadoPorOtroIngreso,

    /// <summary>Pasó la duración máxima sin egreso.</summary>
    SuperoDuracionMaxima,
}

public enum MotivoEgresoSinPareja
{
    /// <summary>No había un ingreso abierto de la misma patente.</summary>
    SinIngresoPrevio,

    /// <summary>Había un ingreso, pero más viejo que la duración máxima.</summary>
    IngresoDemasiadoAntiguo,
}

public sealed record IngresoSinPareja(Guid EventId, DateTimeOffset Ts, MotivoIngresoSinPareja Motivo);

public sealed record EgresoSinPareja(Guid EventId, DateTimeOffset Ts, MotivoEgresoSinPareja Motivo);

public sealed record ResultadoEmparejamiento
{
    public required ReglaEstadias Regla { get; init; }
    public required DateTimeOffset Corte { get; init; }
    public required IReadOnlyList<Estadia> Estadias { get; init; }
    public required IReadOnlyList<IngresoSinPareja> IngresosSinPareja { get; init; }
    public required IReadOnlyList<EgresoSinPareja> EgresosSinPareja { get; init; }

    /// <summary>Vehículos sin patente o con patente inválida: cuentan en el conteo, no en las estadías.</summary>
    public required IReadOnlyList<Guid> SinPatenteValida { get; init; }
}

/// <summary>
/// Empareja ingresos con egresos por sede y patente.
///
/// Regla, completa:
/// 1. Solo participan eventos con patente válida. El resto va a
///    <see cref="ResultadoEmparejamiento.SinPatenteValida"/>.
/// 2. La clave es (sede, país, patente normalizada).
/// 3. Los eventos se procesan en orden de hora (UTC); a igual hora, el ingreso va
///    antes que el egreso, y después por <c>eventId</c>, para que el resultado no
///    dependa del orden de entrada.
/// 4. Un ingreso abre la estadía. Si ya había una abierta para esa patente, la
///    anterior queda sin pareja (<see cref="MotivoIngresoSinPareja.ReemplazadoPorOtroIngreso"/>).
/// 5. Un egreso cierra la estadía abierta si la diferencia no supera la duración
///    máxima. Si la supera, los dos quedan sin pareja. Si no hay ninguna abierta,
///    el egreso queda sin pareja.
/// 6. Al terminar, cada ingreso abierto queda <see cref="MotivoIngresoSinPareja.Abierto"/>
///    si desde su hora hasta el corte no pasó la duración máxima, y
///    <see cref="MotivoIngresoSinPareja.SuperoDuracionMaxima"/> si pasó.
/// 7. Un mismo <c>eventId</c> repetido se cuenta una sola vez.
/// </summary>
public static class EmparejadorEstadias
{
    public static ResultadoEmparejamiento Emparejar(
        IEnumerable<EventoVehiculo> eventos, ReglaEstadias regla, DateTimeOffset corte)
    {
        if (regla.DuracionMaxima <= TimeSpan.Zero)
        {
            throw new ArgumentException("La duración máxima tiene que ser positiva.", nameof(regla));
        }

        var unicos = eventos
            .GroupBy(e => e.EventId)
            .Select(g => g.First())
            .ToList();

        var sinPatente = unicos
            .Where(e => e.Patente is not { Valida: true })
            .OrderBy(e => e.Ts.UtcDateTime).ThenBy(e => e.EventId)
            .Select(e => e.EventId)
            .ToList();

        var ordenados = unicos
            .Where(e => e.Patente is { Valida: true })
            .OrderBy(e => e.Ts.UtcDateTime)
            .ThenBy(e => e is VehiculoIngreso ? 0 : 1)
            .ThenBy(e => e.EventId);

        var abiertos = new Dictionary<(string Sede, string Pais, string Patente), EventoVehiculo>();
        var estadias = new List<Estadia>();
        var ingresosSueltos = new List<IngresoSinPareja>();
        var egresosSueltos = new List<EgresoSinPareja>();

        foreach (var evento in ordenados)
        {
            var clave = (evento.Sede, evento.Patente!.Pais, evento.Patente.Normalizada!);
            abiertos.TryGetValue(clave, out var abierto);

            if (evento is VehiculoIngreso)
            {
                if (abierto is not null)
                {
                    ingresosSueltos.Add(new(abierto.EventId, abierto.Ts, MotivoIngresoSinPareja.ReemplazadoPorOtroIngreso));
                }

                abiertos[clave] = evento;
                continue;
            }

            if (abierto is null)
            {
                egresosSueltos.Add(new(evento.EventId, evento.Ts, MotivoEgresoSinPareja.SinIngresoPrevio));
                continue;
            }

            abiertos.Remove(clave);
            if (evento.Ts - abierto.Ts > regla.DuracionMaxima)
            {
                ingresosSueltos.Add(new(abierto.EventId, abierto.Ts, MotivoIngresoSinPareja.SuperoDuracionMaxima));
                egresosSueltos.Add(new(evento.EventId, evento.Ts, MotivoEgresoSinPareja.IngresoDemasiadoAntiguo));
                continue;
            }

            estadias.Add(new Estadia
            {
                Sede = clave.Sede,
                Pais = clave.Pais,
                Patente = clave.Item3,
                EventoIngreso = abierto.EventId,
                EventoEgreso = evento.EventId,
                Ingreso = abierto.Ts,
                Egreso = evento.Ts,
            });
        }

        foreach (var abierto in abiertos.Values.OrderBy(e => e.Ts.UtcDateTime).ThenBy(e => e.EventId))
        {
            var motivo = corte - abierto.Ts > regla.DuracionMaxima
                ? MotivoIngresoSinPareja.SuperoDuracionMaxima
                : MotivoIngresoSinPareja.Abierto;
            ingresosSueltos.Add(new(abierto.EventId, abierto.Ts, motivo));
        }

        return new ResultadoEmparejamiento
        {
            Regla = regla,
            Corte = corte,
            Estadias = estadias,
            IngresosSinPareja = ingresosSueltos.OrderBy(i => i.Ts.UtcDateTime).ThenBy(i => i.EventId).ToList(),
            EgresosSinPareja = egresosSueltos,
            SinPatenteValida = sinPatente,
        };
    }
}
