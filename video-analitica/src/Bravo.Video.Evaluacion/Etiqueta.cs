namespace Bravo.Video.Evaluacion;

public enum Periodo
{
    Dia,
    Noche,
}

/// <summary>Una fila de la planilla de etiquetado (la que llenaría una persona mirando el video).</summary>
/// <param name="EventId">Evento que corresponde a la pasada. Vacío si el motor no la detectó.</param>
/// <param name="PatenteReal">Patente que se ve en el video. "?" si no se lee ni a ojo; "-" si no había vehículo.</param>
public sealed record Etiqueta(Guid? EventId, DateTimeOffset Ts, string PatenteReal, Periodo Periodo)
{
    public const string Ilegible = "?";
    public const string SinVehiculo = "-";
}
