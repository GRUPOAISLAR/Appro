using System.Text.Json;

namespace Bravo.Video.Lectores;

/// <summary>
/// Traduce el reporte de cruce de línea (<c>line_trigger_data</c>) de un sensor
/// de conteo tipo Milesight VS13x, enviado por HTTP.
///
/// Cada entrada trae el nombre o número de la línea y las cantidades <c>in</c> y
/// <c>out</c> del intervalo. La hora del intervalo no se toma del reporte: su
/// formato cambia por modelo y firmware, así que el conector usa la hora en que lo
/// recibió. Validar contra una captura real del modelo que se instale.
/// </summary>
public static class LectorMilesight
{
    public static IReadOnlyList<ConteoLinea> Leer(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new MensajeInvalidoException("Reporte del sensor con JSON inválido.", e);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("line_trigger_data", out var lineas)
                || lineas.ValueKind != JsonValueKind.Array)
            {
                throw new MensajeInvalidoException("Reporte del sensor sin 'line_trigger_data'.");
            }

            var conteos = new List<ConteoLinea>();
            foreach (var linea in lineas.EnumerateArray())
            {
                var nombre = NombreLinea(linea)
                    ?? throw new MensajeInvalidoException("Línea sin 'line_name' ni 'line'.");
                var entradas = Entero(linea, "in");
                var salidas = Entero(linea, "out");
                conteos.Add(new ConteoLinea(nombre, entradas, salidas));
            }

            return conteos;
        }
    }

    private static string? NombreLinea(JsonElement linea)
    {
        if (linea.TryGetProperty("line_name", out var n) && n.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(n.GetString()))
        {
            return n.GetString()!.Trim();
        }

        if (linea.TryGetProperty("line", out var l))
        {
            return l.ValueKind switch
            {
                JsonValueKind.Number => l.GetRawText(),
                JsonValueKind.String when !string.IsNullOrWhiteSpace(l.GetString()) => l.GetString()!.Trim(),
                _ => null,
            };
        }

        return null;
    }

    private static int Entero(JsonElement e, string nombre)
    {
        if (!e.TryGetProperty(nombre, out var v) || v.ValueKind != JsonValueKind.Number
            || !v.TryGetInt32(out var n) || n < 0)
        {
            throw new MensajeInvalidoException($"Línea con '{nombre}' ausente o no es un entero no negativo.");
        }

        return n;
    }
}
