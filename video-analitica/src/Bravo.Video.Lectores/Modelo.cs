using Bravo.Video.Patentes;

namespace Bravo.Video.Lectores;

/// <summary>
/// Lectura de un vehículo tal como la entrega un motor, antes de decidir si es
/// ingreso o egreso. Cada lector traduce el formato de su motor a esto, y
/// <see cref="ArmadorEventos"/> lo convierte en un evento del contrato.
/// </summary>
public sealed record LecturaVehiculo
{
    public required string Motor { get; init; }
    public required string VersionMotor { get; init; }
    public required string Camara { get; init; }

    /// <summary>Id del objeto o la lectura en el motor. Es la base del id del evento.</summary>
    public required string IdOrigen { get; init; }

    public required DateTimeOffset Ts { get; init; }

    /// <summary>Texto crudo de la patente. Null si el motor vio el vehículo pero no la leyó.</summary>
    public string? PatenteLeida { get; init; }

    /// <summary>Confianza de la lectura entre 0 y 1, ya convertida desde la escala del motor.</summary>
    public decimal? Confianza { get; init; }

    /// <summary>Sentido de circulación que informa la cámara ANPR ("forward", "reverse"…).</summary>
    public string? Sentido { get; init; }

    /// <summary>Zonas de Frigate por las que pasó el objeto.</summary>
    public IReadOnlyList<string> Zonas { get; init; } = [];
}

/// <summary>Personas que cruzaron una línea, según el sensor de conteo.</summary>
public sealed record ConteoLinea(string Linea, int Entradas, int Salidas);

public static class Motores
{
    public const string Frigate = "frigate";
    public const string HikvisionAnpr = "hikvision-anpr";
    public const string Milesight = "milesight";
}

public enum RolCamara
{
    /// <summary>Todo lo que ve la cámara es ingreso.</summary>
    Ingreso,

    /// <summary>Todo lo que ve la cámara es egreso.</summary>
    Egreso,

    /// <summary>Decide la zona de Frigate por la que pasó el objeto.</summary>
    PorZona,

    /// <summary>Decide el sentido de circulación que informa la cámara ANPR.</summary>
    PorSentido,
}

/// <summary>
/// Cómo se interpreta cada cámara. Los valores de zona y sentido se comparan
/// sin distinguir mayúsculas.
/// </summary>
public sealed record ConfiguracionCamara
{
    public required string Camara { get; init; }
    public required RolCamara Rol { get; init; }
    public string? ZonaIngreso { get; init; }
    public string? ZonaEgreso { get; init; }
    public string? SentidoIngreso { get; init; }
    public string? SentidoEgreso { get; init; }

    /// <summary>
    /// Zona de Frigate por la que tiene que haber pasado el objeto para contar,
    /// con cualquier rol. Sirve para que un auto estacionado a la vista de la
    /// cámara no se cuente como ingreso: solo cuenta lo que cruza el carril.
    /// </summary>
    public string? ZonaRequerida { get; init; }
}

public sealed record ConfiguracionSede
{
    public required string Sede { get; init; }
    public required string Dispositivo { get; init; }
    public required Pais Pais { get; init; }

    /// <summary>Zona horaria de la sede: las horas de los eventos se expresan en ella.</summary>
    public required TimeZoneInfo ZonaHoraria { get; init; }

    public required IReadOnlyDictionary<string, ConfiguracionCamara> Camaras { get; init; }
}

/// <summary>Mensaje de un motor que no respeta el formato esperado.</summary>
/// <remarks>
/// El mensaje de la excepción nunca incluye el contenido recibido: puede traer
/// una patente, y las excepciones terminan en los logs.
/// </remarks>
public sealed class MensajeInvalidoException(string motivo, Exception? interna = null)
    : FormatException(motivo, interna);
