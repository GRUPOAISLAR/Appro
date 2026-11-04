using System.Text.Json.Serialization;

namespace Bravo.Video.Contratos;

/// <summary>
/// Contrato de eventos v1: lo que el conector del local le manda a la ingesta.
///
/// Todos los eventos comparten el sobre (identificador, versión, sede, equipo,
/// cámara, hora y motor que lo generó) y suman los datos propios de su tipo. El
/// tipo viaja en la propiedad <c>tipo</c> del JSON y lo resuelve el serializador:
/// no hay una propiedad <c>Tipo</c> en C# para que no puedan contradecirse.
///
/// Nunca viajan imágenes: solo el texto de la patente leída y su confianza.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tipo",
    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(VehiculoIngreso), TiposEvento.VehiculoIngreso)]
[JsonDerivedType(typeof(VehiculoEgreso), TiposEvento.VehiculoEgreso)]
[JsonDerivedType(typeof(PersonasCruce), TiposEvento.PersonasCruce)]
[JsonDerivedType(typeof(BoxOcupado), TiposEvento.BoxOcupado)]
[JsonDerivedType(typeof(BoxLibre), TiposEvento.BoxLibre)]
[JsonDerivedType(typeof(DispositivoLatido), TiposEvento.DispositivoLatido)]
public abstract record EventoVideo
{
    /// <summary>
    /// Identificador del evento. Lo deriva el conector de forma determinística
    /// (<see cref="IdEvento"/>) para que un reenvío produzca el mismo id y la
    /// ingesta lo descarte en vez de duplicarlo.
    /// </summary>
    public required Guid EventId { get; init; }

    public string SchemaVersion { get; init; } = VersionContrato.Actual;

    /// <summary>Código de la sede (ej. "AR-PRUEBA-01"). Lo fija la configuración del conector.</summary>
    public required string Sede { get; init; }

    /// <summary>Equipo que generó el evento (el mini PC del local).</summary>
    public required string Dispositivo { get; init; }

    /// <summary>Cámara o sensor de origen. Null solo en el latido del equipo.</summary>
    public string? Camara { get; init; }

    /// <summary>
    /// Hora del hecho con su zona horaria explícita. El JSON se rechaza si la hora
    /// viene sin desplazamiento (ver <see cref="ConvertidorFechaConZona"/>).
    /// </summary>
    public required DateTimeOffset Ts { get; init; }

    public required Fuente Fuente { get; init; }
}

/// <summary>Motor que produjo el dato y su versión, para poder comparar motores con datos.</summary>
public sealed record Fuente(string Motor, string Version);

/// <summary>
/// Patente tal como se leyó y tal como quedó después de normalizar.
/// Se guardan las dos para poder auditar cada corrección.
/// </summary>
public sealed record LecturaPatente
{
    /// <summary>Texto que devolvió el motor, sin tocar.</summary>
    public required string Leida { get; init; }

    /// <summary>Texto normalizado. Null si no encajó en ningún formato del país.</summary>
    public string? Normalizada { get; init; }

    /// <summary>País contra el que se validó: "AR" o "MX".</summary>
    public required string Pais { get; init; }

    /// <summary>Nombre del formato reconocido (ej. "AR_MERCOSUR_AUTO"). Null si no es válido.</summary>
    public string? Formato { get; init; }

    public required bool Valida { get; init; }

    /// <summary>Cantidad de caracteres corregidos por parecido (O/0, I/1…). Cero si se leyó limpia.</summary>
    public int Correcciones { get; init; }

    /// <summary>Confianza que informó el motor, entre 0 y 1. Null si el motor no la da.</summary>
    public decimal? Confianza { get; init; }
}

public abstract record EventoVehiculo : EventoVideo
{
    /// <summary>Null si el motor vio un vehículo pero no pudo leer la patente.</summary>
    public LecturaPatente? Patente { get; init; }

    /// <summary>Identificador del objeto o la lectura en el motor (id de Frigate, UUID de la cámara).</summary>
    public string? IdOrigen { get; init; }
}

public sealed record VehiculoIngreso : EventoVehiculo;

public sealed record VehiculoEgreso : EventoVehiculo;

/// <summary>Personas que cruzaron una línea virtual en un intervalo.</summary>
public sealed record PersonasCruce : EventoVideo
{
    public required string Linea { get; init; }
    public required int Entradas { get; init; }
    public required int Salidas { get; init; }

    /// <summary>Inicio del intervalo que informa el sensor, si lo informa.</summary>
    public DateTimeOffset? Desde { get; init; }

    /// <summary>Fin del intervalo que informa el sensor, si lo informa.</summary>
    public DateTimeOffset? Hasta { get; init; }
}

public abstract record EventoBox : EventoVideo
{
    public required string Box { get; init; }
    public string? IdOrigen { get; init; }
}

public sealed record BoxOcupado : EventoBox;

public sealed record BoxLibre : EventoBox;

/// <summary>Estado del equipo del local. Se manda cada minuto aunque no haya tránsito.</summary>
public sealed record DispositivoLatido : EventoVideo
{
    public required string VersionConector { get; init; }
    public required IReadOnlyList<EstadoMotor> Motores { get; init; }

    /// <summary>Eventos guardados en la cola de disco que todavía no se pudieron enviar.</summary>
    public required int ColaPendiente { get; init; }

    /// <summary>
    /// Diferencia entre el reloj del equipo y el del servidor, en milisegundos
    /// (positivo: el equipo adelanta). Null hasta el primer envío exitoso.
    /// </summary>
    public long? DesvioRelojMs { get; init; }
}

public sealed record EstadoMotor(string Nombre, string Estado);

public static class EstadosMotor
{
    public const string EnLinea = "en_linea";
    public const string FueraDeLinea = "fuera_de_linea";
    public const string Desconocido = "desconocido";
}

public static class TiposEvento
{
    public const string VehiculoIngreso = "VEHICULO_INGRESO";
    public const string VehiculoEgreso = "VEHICULO_EGRESO";
    public const string PersonasCruce = "PERSONAS_CRUCE";
    public const string BoxOcupado = "BOX_OCUPADO";
    public const string BoxLibre = "BOX_LIBRE";
    public const string DispositivoLatido = "DISPOSITIVO_LATIDO";

    public static readonly IReadOnlyList<string> Todos =
    [
        VehiculoIngreso, VehiculoEgreso, PersonasCruce, BoxOcupado, BoxLibre, DispositivoLatido,
    ];

    public static string De(EventoVideo evento) => evento switch
    {
        global::Bravo.Video.Contratos.VehiculoIngreso => VehiculoIngreso,
        global::Bravo.Video.Contratos.VehiculoEgreso => VehiculoEgreso,
        global::Bravo.Video.Contratos.PersonasCruce => PersonasCruce,
        global::Bravo.Video.Contratos.BoxOcupado => BoxOcupado,
        global::Bravo.Video.Contratos.BoxLibre => BoxLibre,
        global::Bravo.Video.Contratos.DispositivoLatido => DispositivoLatido,
        _ => throw new ArgumentOutOfRangeException(nameof(evento), evento.GetType().Name, "Tipo de evento sin código."),
    };
}

public static class VersionContrato
{
    public const string Actual = "1";
}
