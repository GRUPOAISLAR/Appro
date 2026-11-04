using System.Security.Cryptography;
using System.Text;

namespace Bravo.Video.Contratos;

/// <summary>
/// Identificador determinístico de un evento.
///
/// El conector reenvía lo que quedó en la cola cuando vuelve internet, y puede
/// volver a procesar un mensaje de MQTT si se reinicia. Si el id fuera aleatorio,
/// cada reenvío sería un evento nuevo para la ingesta. Derivándolo de lo que
/// identifica al hecho (sede, equipo, motor, id en el motor y tipo), el mismo
/// hecho siempre da el mismo id.
///
/// Es un UUID versión 8 (formato libre, RFC 9562) con los primeros 16 bytes de un
/// SHA-256 de las partes. No usar para nada que necesite secreto: no lo es.
/// </summary>
public static class IdEvento
{
    // Separador de unidades ASCII: no aparece en ninguna de las partes, así
    // ("ab", "c") y ("a", "bc") no colisionan.
    private const char Separador = '\u001F';

    public static Guid Derivar(string sede, string dispositivo, string motor, string idOrigen, string tipo)
    {
        string[] partes = [sede, dispositivo, motor, idOrigen, tipo];
        foreach (var parte in partes)
        {
            if (string.IsNullOrWhiteSpace(parte))
            {
                throw new ArgumentException("Ninguna parte del id puede estar vacía.");
            }

            if (parte.Contains(Separador))
            {
                throw new ArgumentException("Una parte del id contiene el separador reservado.");
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(Separador, partes)));
        var bytes = hash.AsSpan(0, 16).ToArray();

        // Versión 8 en el nibble alto del byte 6 y variante RFC (10xx) en el byte 8.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }
}
