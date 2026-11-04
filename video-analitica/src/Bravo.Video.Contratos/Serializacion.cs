using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bravo.Video.Contratos;

/// <summary>
/// Opciones JSON del contrato. Conector e ingesta usan estas mismas: si cada lado
/// arma las suyas, tarde o temprano difieren en algo (mayúsculas, fechas) y el
/// error aparece en producción.
/// </summary>
public static class OpcionesJson
{
    public static readonly JsonSerializerOptions Eventos = Crear();

    private static JsonSerializerOptions Crear()
    {
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            // El discriminador "tipo" puede no venir primero si otro sistema arma el JSON.
            AllowOutOfOrderMetadataProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };
        opciones.Converters.Add(new ConvertidorFechaConZona());
        opciones.Converters.Add(new ConvertidorFechaConZonaOpcional());
        opciones.MakeReadOnly(populateMissingResolver: true);
        return opciones;
    }
}

/// <summary>
/// Exige que toda hora del contrato traiga zona horaria explícita ("Z" o "±hh:mm").
///
/// System.Text.Json acepta "2026-11-04T10:00:00" y le pone en silencio la zona de
/// la máquina que deserializa. Con el equipo en el local y el servidor en otra
/// zona, eso corre las estadías varias horas sin que nada falle.
/// </summary>
public sealed class ConvertidorFechaConZona : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var texto = reader.GetString() ?? throw new JsonException("Fecha nula.");
        return Interpretar(texto);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture));

    internal static DateTimeOffset Interpretar(string texto)
    {
        if (!TieneZona(texto))
        {
            throw new JsonException("La fecha no tiene zona horaria explícita (falta 'Z' o '±hh:mm').");
        }

        if (!DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            throw new JsonException("Fecha con formato inválido.");
        }

        return fecha;
    }

    internal static bool TieneZona(string texto)
    {
        var t = texto.Trim();
        if (t.EndsWith('Z') || t.EndsWith('z'))
        {
            return true;
        }

        // ±hh:mm al final, después de la parte de hora.
        var separador = t.IndexOf('T');
        if (separador < 0)
        {
            return false;
        }

        var hora = t[(separador + 1)..];
        return hora.Length >= 6 && (hora[^6] == '+' || hora[^6] == '-') && hora[^3] == ':';
    }
}

public sealed class ConvertidorFechaConZonaOpcional : JsonConverter<DateTimeOffset?>
{
    public override bool HandleNull => true;

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var texto = reader.GetString() ?? throw new JsonException("Fecha nula.");
        return ConvertidorFechaConZona.Interpretar(texto);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
    }
}
