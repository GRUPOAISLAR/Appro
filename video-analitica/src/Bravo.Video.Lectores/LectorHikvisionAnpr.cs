using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Bravo.Video.Lectores;

/// <summary>
/// Traduce la alerta <c>EventNotificationAlert</c> que una cámara Hikvision con
/// ANPR envía por HTTP en cada lectura.
///
/// La cámara manda un multipart con el XML y las imágenes; el conector extrae
/// solo la parte XML y descarta las imágenes sin guardarlas. Este lector recibe
/// ese XML.
///
/// Los elementos se buscan por nombre local: según el firmware el espacio de
/// nombres es <c>hikvision.com/ver20</c> o <c>isapi.org/ver20</c>.
/// </summary>
public static class LectorHikvisionAnpr
{
    /// <summary>Texto que pone la cámara cuando detectó un vehículo pero no leyó la patente.</summary>
    private static readonly HashSet<string> SinLectura = new(StringComparer.OrdinalIgnoreCase) { "unknown", "noplate", "" };

    /// <param name="xml">Parte XML de la alerta.</param>
    /// <param name="camara">Cámara que envió, según la URL a la que llamó (la configura el conector).</param>
    /// <param name="versionFirmware">Firmware de la cámara, desde la configuración.</param>
    /// <param name="zonaSede">Para interpretar la hora si la cámara la manda sin desplazamiento.</param>
    /// <returns>Null si la alerta no es de ANPR (latido, pérdida de video, etc.).</returns>
    public static LecturaVehiculo? Leer(string xml, string camara, string versionFirmware, TimeZoneInfo zonaSede)
    {
        XElement raiz;
        try
        {
            var opciones = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var lector = XmlReader.Create(new StringReader(xml), opciones);
            raiz = XElement.Load(lector);
        }
        catch (XmlException e)
        {
            throw new MensajeInvalidoException("Alerta de Hikvision con XML inválido.", e);
        }

        if (raiz.Name.LocalName != "EventNotificationAlert")
        {
            throw new MensajeInvalidoException("El XML no es una EventNotificationAlert.");
        }

        if (!string.Equals(Hijo(raiz, "eventType"), "ANPR", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var anpr = raiz.Elements().FirstOrDefault(e => e.Name.LocalName == "ANPR")
            ?? throw new MensajeInvalidoException("Alerta ANPR sin el bloque <ANPR>.");

        var textoFecha = Hijo(raiz, "dateTime") ?? throw new MensajeInvalidoException("Alerta ANPR sin <dateTime>.");
        var ts = InterpretarFecha(textoFecha, zonaSede);

        var patente = Hijo(anpr, "licensePlate")?.Trim();
        if (patente is not null && SinLectura.Contains(patente))
        {
            patente = null;
        }

        decimal? confianza = null;
        if (Hijo(anpr, "confidenceLevel") is { } textoConfianza)
        {
            if (!decimal.TryParse(textoConfianza, NumberStyles.Number, CultureInfo.InvariantCulture, out var nivel)
                || nivel < 0m || nivel > 100m)
            {
                throw new MensajeInvalidoException("confidenceLevel fuera de 0–100.");
            }

            // La cámara informa en porcentaje; el contrato usa 0–1.
            confianza = nivel / 100m;
        }

        // Si la cámara no manda UUID, el id sale de la hora y el texto leído:
        // es lo que hace única a una lectura de la misma cámara.
        var idOrigen = Hijo(raiz, "UUID")
            ?? $"{ts.ToUnixTimeMilliseconds()}:{patente ?? "-"}";

        return new LecturaVehiculo
        {
            Motor = Motores.HikvisionAnpr,
            VersionMotor = versionFirmware,
            Camara = camara,
            IdOrigen = idOrigen,
            Ts = ts,
            PatenteLeida = patente,
            Confianza = confianza,
            Sentido = Hijo(anpr, "direction"),
        };
    }

    private static DateTimeOffset InterpretarFecha(string texto, TimeZoneInfo zonaSede)
    {
        var t = texto.Trim();
        if (DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var conZona)
            && TieneZona(t))
        {
            return conZona;
        }

        // Sin desplazamiento: la hora es la local de la cámara, que debe coincidir con la de la sede (NTP).
        if (!DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            throw new MensajeInvalidoException("<dateTime> con formato inválido.");
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zonaSede.GetUtcOffset(local));
    }

    private static bool TieneZona(string t)
    {
        if (t.EndsWith('Z') || t.EndsWith('z'))
        {
            return true;
        }

        var hora = t.IndexOf('T') is var i and >= 0 ? t[(i + 1)..] : t;
        return hora.Length >= 6 && (hora[^6] is '+' or '-') && hora[^3] == ':';
    }

    private static string? Hijo(XElement padre, string nombre)
    {
        var valor = padre.Elements().FirstOrDefault(e => e.Name.LocalName == nombre)?.Value;
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }
}
