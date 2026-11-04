namespace Bravo.Video.Ingesta.Datos;

/// <summary>Equipo de un local autorizado a enviar eventos.</summary>
public sealed class Equipo
{
    /// <summary>Mismo valor que el campo <c>dispositivo</c> de los eventos.</summary>
    public required string Dispositivo { get; set; }

    public required string Sede { get; set; }

    /// <summary>SHA-256 del token, en hexadecimal. El token en claro no se guarda.</summary>
    public required string TokenHash { get; set; }

    public bool Activo { get; set; } = true;
    public DateTime CreadoUtc { get; set; }
    public DateTime? UltimoContactoUtc { get; set; }
}

/// <summary>
/// Un evento del contrato v1 guardado en columnas, una tabla para todos los tipos.
///
/// Se guarda en columnas y no como JSON para que la anonimización pueda borrar la
/// patente con un UPDATE simple y verificable, sin reescribir documentos. No hay
/// copia del JSON original: tendría la patente en texto.
///
/// Las horas van en UTC más el desplazamiento original en minutos, porque SQLite
/// no ordena ni compara <c>DateTimeOffset</c>.
/// </summary>
public sealed class EventoGuardado
{
    public Guid EventId { get; set; }
    public required string Tipo { get; set; }
    public required string SchemaVersion { get; set; }
    public required string Sede { get; set; }
    public required string Dispositivo { get; set; }
    public string? Camara { get; set; }
    public DateTime TsUtc { get; set; }
    public int TsOffsetMinutos { get; set; }
    public DateTime RecibidoUtc { get; set; }
    public required string Motor { get; set; }
    public required string VersionMotor { get; set; }
    public string? IdOrigen { get; set; }

    // Vehículo
    public string? PatentePais { get; set; }
    public string? PatenteLeida { get; set; }
    public string? PatenteNormalizada { get; set; }

    /// <summary>HMAC de la patente normalizada. Se calcula al recibir y queda después de anonimizar.</summary>
    public string? PatenteHash { get; set; }

    public bool? PatenteValida { get; set; }
    public string? PatenteFormato { get; set; }
    public int? PatenteCorrecciones { get; set; }
    public decimal? PatenteConfianza { get; set; }

    /// <summary>Cuándo se borró la patente en texto. Null mientras se conserva.</summary>
    public DateTime? PatenteAnonimizadaUtc { get; set; }

    // Personas
    public string? Linea { get; set; }
    public int? Entradas { get; set; }
    public int? Salidas { get; set; }
    public DateTime? DesdeUtc { get; set; }
    public DateTime? HastaUtc { get; set; }

    // Box
    public string? Box { get; set; }

    // Latido
    public string? VersionConector { get; set; }
    public int? ColaPendiente { get; set; }
    public long? DesvioRelojMs { get; set; }

    /// <summary>Estado de cada motor en JSON: [{"nombre":"frigate","estado":"en_linea"}].</summary>
    public string? MotoresJson { get; set; }
}
