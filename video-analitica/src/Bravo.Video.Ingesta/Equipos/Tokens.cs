using System.Security.Cryptography;
using System.Text;

namespace Bravo.Video.Ingesta.Equipos;

/// <summary>
/// Tokens de los equipos. Son 32 bytes aleatorios: con esa entropía alcanza un
/// SHA-256 simple para guardarlos (no hace falta un hash lento como con una
/// contraseña elegida por una persona).
/// </summary>
public static class Tokens
{
    public const string Prefijo = "bvv_";

    public static string Generar() =>
        Prefijo + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
