using System.Globalization;
using System.Text.RegularExpressions;
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

        // Si la cámara no manda UUID, el id sale solo de la hora (la cámara ya entra
        // en el eventId). NUNCA de la patente: el idOrigen y el eventId se conservan
        // después de anonimizar, y con la patente adentro se la podría recuperar
        // probando combinaciones. Límite conocido: dos lecturas de la misma cámara en
        // el mismo segundo sin UUID darían el mismo id y la segunda se descartaría.
        var idOrigen = Hijo(raiz, "UUID") ?? ts.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

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

    // Hora con fracción opcional y desplazamiento opcional: Z, ±hh:mm, ±hhmm o ±hh.
    private static readonly Regex FormatoFecha = new(
        @"^(?<base>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)(?<zona>Z|z|[+-]\d{2}(?::?\d{2})?)?$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Interpreta la hora de la alerta sin depender de la zona de la máquina. Con
    /// desplazamiento (en cualquiera de las formas que usan los firmwares) se
    /// respeta; sin desplazamiento, la hora es la local de la cámara y se toma en la
    /// zona de la sede. Cualquier otro formato se rechaza.
    /// </summary>
    internal static DateTimeOffset InterpretarFecha(string texto, TimeZoneInfo zonaSede)
    {
        var m = FormatoFecha.Match(texto.Trim());
        if (!m.Success)
        {
            throw new MensajeInvalidoException("<dateTime> con formato inválido.");
        }

        var local = DateTime.ParseExact(m.Groups["base"].Value, "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
            CultureInfo.InvariantCulture, DateTimeStyles.None);

        if (!m.Groups["zona"].Success)
        {
            // Sin desplazamiento: hora local de la cámara, que debe coincidir con la de la sede (NTP).
            return new DateTimeOffset(local, zonaSede.GetUtcOffset(local));
        }

        var zona = m.Groups["zona"].Value;
        if (zona is "Z" or "z")
        {
            return new DateTimeOffset(local, TimeSpan.Zero);
        }

        var signo = zona[0] == '-' ? -1 : 1;
        var digitos = zona[1..].Replace(":", string.Empty);
        var horas = int.Parse(digitos[..2], CultureInfo.InvariantCulture);
        var minutos = digitos.Length == 4 ? int.Parse(digitos[2..], CultureInfo.InvariantCulture) : 0;
        if (horas > 14 || minutos > 59)
        {
            throw new MensajeInvalidoException("<dateTime> con desplazamiento inválido.");
        }

        return new DateTimeOffset(local, signo * new TimeSpan(horas, minutos, 0));
    }

    private static string? Hijo(XElement padre, string nombre)
    {
        var valor = padre.Elements().FirstOrDefault(e => e.Name.LocalName == nombre)?.Value;
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }
}
