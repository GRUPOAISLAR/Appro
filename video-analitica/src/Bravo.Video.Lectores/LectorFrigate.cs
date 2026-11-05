using System.Text.Json;

namespace Bravo.Video.Lectores;

/// <summary>Mensaje de <c>frigate/events</c>, reducido a lo que usamos.</summary>
public sealed record MensajeEventoFrigate
{
    /// <summary>"new", "update" o "end".</summary>
    public required string Tipo { get; init; }

    public required string Id { get; init; }
    public required string Camara { get; init; }
    public required string Etiqueta { get; init; }
    public required DateTimeOffset Inicio { get; init; }
    public DateTimeOffset? Fin { get; init; }
    public IReadOnlyList<string> ZonasIngresadas { get; init; } = [];
    public string? Patente { get; init; }
    public decimal? PuntajePatente { get; init; }
}

/// <summary>Mensaje de <c>frigate/tracked_object_update</c> con <c>type: "lpr"</c>.</summary>
public sealed record ActualizacionPatenteFrigate
{
    public required string Id { get; init; }
    public required string Camara { get; init; }
    public required string Patente { get; init; }
    public decimal? Puntaje { get; init; }
    public DateTimeOffset? Ts { get; init; }
}

/// <summary>
/// Traduce los mensajes MQTT de Frigate. No guarda estado: el armado de una
/// lectura completa a partir de varios mensajes lo hace <see cref="AcumuladorFrigate"/>.
///
/// Formato tomado de la documentación de MQTT de Frigate 0.16/0.17. Validar con
/// una captura real de la versión instalada antes de la prueba: los nombres de
/// campos cambiaron entre versiones.
/// </summary>
public static class LectorFrigate
{
    public const string TopicoEventos = "frigate/events";
    public const string TopicoActualizaciones = "frigate/tracked_object_update";
    public const string TopicoDisponible = "frigate/available";

    public static MensajeEventoFrigate LeerEvento(string json)
    {
        using var doc = Parsear(json);
        var raiz = doc.RootElement;
        var tipo = Texto(raiz, "type") ?? throw new MensajeInvalidoException("frigate/events sin 'type'.");
        if (!raiz.TryGetProperty("after", out var after) || after.ValueKind != JsonValueKind.Object)
        {
            throw new MensajeInvalidoException("frigate/events sin 'after'.");
        }

        var inicio = Segundos(after, "start_time") ?? throw new MensajeInvalidoException("frigate/events sin 'start_time'.");
        return new MensajeEventoFrigate
        {
            Tipo = tipo,
            Id = Texto(after, "id") ?? throw new MensajeInvalidoException("frigate/events sin 'id'."),
            Camara = Texto(after, "camera") ?? throw new MensajeInvalidoException("frigate/events sin 'camera'."),
            Etiqueta = Texto(after, "label") ?? throw new MensajeInvalidoException("frigate/events sin 'label'."),
            Inicio = inicio,
            Fin = Segundos(after, "end_time"),
            ZonasIngresadas = Lista(after, "entered_zones"),
            Patente = Texto(after, "recognized_license_plate"),
            PuntajePatente = Numero(after, "recognized_license_plate_score"),
        };
    }

    /// <summary>Devuelve null si la actualización no es de patente (descripción, rostro, etc.).</summary>
    public static ActualizacionPatenteFrigate? LeerActualizacion(string json)
    {
        using var doc = Parsear(json);
        var raiz = doc.RootElement;
        if (Texto(raiz, "type") != "lpr")
        {
            return null;
        }

        var id = Texto(raiz, "id");
        var patente = Texto(raiz, "plate");
        var camara = Texto(raiz, "camera");

        // Sin id no hay forma confiable de asociarla al vehículo: se ignora.
        if (id is null || patente is null || camara is null)
        {
            return null;
        }

        return new ActualizacionPatenteFrigate
        {
            Id = id,
            Camara = camara,
            Patente = patente,
            Puntaje = Numero(raiz, "score"),
            Ts = Segundos(raiz, "timestamp"),
        };
    }

    /// <summary>"online" → true, "offline" → false, cualquier otra cosa → null.</summary>
    public static bool? LeerDisponibilidad(string payload) => payload.Trim() switch
    {
        "online" => true,
        "offline" => false,
        _ => null,
    };

    private static JsonDocument Parsear(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                throw new MensajeInvalidoException("Mensaje de Frigate que no es un objeto JSON.");
            }

            return doc;
        }
        catch (JsonException e)
        {
            throw new MensajeInvalidoException("Mensaje de Frigate con JSON inválido.", e);
        }
    }

    private static string? Texto(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;

    private static decimal? Numero(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

    /// <summary>Frigate manda las horas como segundos Unix con decimales, en UTC.</summary>
    private static DateTimeOffset? Segundos(JsonElement e, string nombre)
    {
        if (Numero(e, nombre) is not { } segundos)
        {
            return null;
        }

        var milisegundos = (long)decimal.Round(segundos * 1000m, MidpointRounding.ToEven);
        return DateTimeOffset.FromUnixTimeMilliseconds(milisegundos);
    }

    private static IReadOnlyList<string> Lista(JsonElement e, string nombre)
    {
        if (!e.TryGetProperty(nombre, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return v.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!)
            .ToList();
    }
}
