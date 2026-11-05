using System.Security.Cryptography;
using System.Text;

namespace Bravo.Video.Patentes;

/// <summary>
/// Reemplazo de la patente en texto por un valor que permite seguir emparejando
/// ingreso con egreso pero no leer la patente.
///
/// Es HMAC-SHA256 con una clave del servidor, no un SHA-256 a secas: con un hash
/// simple, alguien con la base podría recalcular las patentes posibles (son
/// pocos millones) y revertirlo en minutos. Sin la clave no puede.
///
/// La clave se lee de una variable de entorno en el servidor y nunca se guarda en
/// el repo. Si se pierde o se cambia, los hashes viejos dejan de coincidir con
/// los nuevos: rotarla corta la continuidad de las estadías.
/// </summary>
public sealed class HashPatente
{
    public const string Prefijo = "h1:";
    public const int LargoMinimoClave = 32;

    private readonly byte[] _clave;

    public HashPatente(byte[] clave)
    {
        ArgumentNullException.ThrowIfNull(clave);
        if (clave.Length < LargoMinimoClave)
        {
            throw new ArgumentException($"La clave tiene que tener al menos {LargoMinimoClave} bytes.", nameof(clave));
        }

        _clave = (byte[])clave.Clone();
    }

    /// <summary>
    /// Hash de una patente ya normalizada. El país entra en el cálculo para que
    /// "ABC123" de Argentina y de México no den lo mismo.
    /// </summary>
    public string Calcular(string patenteNormalizada, Pais pais)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patenteNormalizada);
        var datos = Encoding.UTF8.GetBytes($"{pais}:{patenteNormalizada}");
        var hash = HMACSHA256.HashData(_clave, datos);
        return Prefijo + Convert.ToHexStringLower(hash);
    }

    public static bool EsHash(string valor) => valor.StartsWith(Prefijo, StringComparison.Ordinal);
}
