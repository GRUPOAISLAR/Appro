using Bravo.Video.Contratos;
using Bravo.Video.Evaluacion;
using Bravo.Video.Lectores;

namespace Bravo.Video.Simulacion;

/// <summary>Dónde se "instaló" la simulación: tiene que coincidir con el conector.json usado.</summary>
public sealed record InstalacionSimulada
{
    public required ConfiguracionSede Sede { get; init; }
    public required string Motor { get; init; }

    /// <summary>Frigate: una cámara con dos zonas. ANPR: una cámara de ingreso y otra de egreso.</summary>
    public required string CamaraIngreso { get; init; }

    public required string CamaraEgreso { get; init; }
    public string ZonaIngreso { get; init; } = "entrada";
    public string ZonaEgreso { get; init; } = "salida";

    public string CamaraDe(Pasada p) => p.EsIngreso ? CamaraIngreso : CamaraEgreso;

    /// <summary>
    /// El eventId que va a generar el conector para esta pasada. Se calcula igual
    /// que <see cref="ArmadorEventos"/>, así la verdad se cruza con lo que llegó a
    /// la ingesta sin depender de la patente.
    /// </summary>
    public Guid EventIdDe(Pasada p) => IdEvento.Derivar(
        Sede.Sede,
        Sede.Dispositivo,
        $"{Motor}:{CamaraDe(p)}",
        Mensajes.IdOrigen(p, Motor),
        p.EsIngreso ? TiposEvento.VehiculoIngreso : TiposEvento.VehiculoEgreso);
}

public static class Verdad
{
    public static IReadOnlyList<Etiqueta> Etiquetas(IEnumerable<Pasada> pasadas, InstalacionSimulada instalacion) =>
        pasadas
            .Select(p => new Etiqueta(p.Detectada ? instalacion.EventIdDe(p) : null, p.Ts, p.PatenteReal, p.Periodo))
            .ToList();
}
