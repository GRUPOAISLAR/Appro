using System.Text;

namespace Bravo.Video.Patentes;

public enum Pais
{
    AR,
    MX,
}

/// <summary>
/// Un formato de patente como secuencia de posiciones: <c>L</c> es letra y
/// <c>N</c> es número. "LLNNNLL" es el Mercosur de autos (AB123CD).
/// </summary>
public sealed record FormatoPatente(string Nombre, Pais Pais, string Mascara);

/// <summary>Un carácter que se cambió por su parecido, con la posición (base 0) para poder auditarlo.</summary>
public sealed record Correccion(int Posicion, char De, char A);

public sealed record ResultadoPatente
{
    public required string Leida { get; init; }

    /// <summary>Texto en mayúsculas sin espacios, guiones ni puntos, antes de corregir.</summary>
    public required string Limpia { get; init; }

    public required Pais Pais { get; init; }

    /// <summary>Patente final. Null si no es válida.</summary>
    public string? Normalizada { get; init; }

    public FormatoPatente? Formato { get; init; }

    public IReadOnlyList<Correccion> Correcciones { get; init; } = [];

    /// <summary>
    /// True si dos formatos quedaron empatados con la misma cantidad de
    /// correcciones. En ese caso no se elige ninguno: la patente queda inválida.
    /// </summary>
    public bool Ambigua { get; init; }

    public bool Valida => Normalizada is not null;
}

/// <summary>
/// Normaliza y valida patentes leídas por un motor de reconocimiento.
///
/// Regla, en este orden:
/// 1. Se pasa a mayúsculas y se sacan espacios, guiones y puntos. Cualquier otro
///    carácter que no sea letra A–Z o dígito invalida la lectura.
/// 2. Se prueba cada formato del país con el mismo largo. En las posiciones de
///    letra, un dígito parecido se cambia por su letra; en las de número, una
///    letra parecida se cambia por su dígito. Los pares son solo los de
///    <see cref="Parecidos"/>; cualquier otro desajuste descarta el formato.
/// 3. Gana el formato que necesitó menos correcciones. Si dos empatan, la lectura
///    es ambigua y no se elige: queda inválida.
///
/// No hay otras reglas implícitas. Una patente que no encaja en ningún formato se
/// devuelve inválida, con el texto leído, para que se vea en el tablero.
/// </summary>
public static class NormalizadorPatentes
{
    /// <summary>Pares letra ↔ dígito que los motores confunden.</summary>
    public static readonly IReadOnlyList<(char Letra, char Digito)> Parecidos =
    [
        ('O', '0'),
        ('I', '1'),
        ('B', '8'),
        ('S', '5'),
        ('Z', '2'),
        ('G', '6'),
    ];

    public static readonly IReadOnlyList<FormatoPatente> Formatos =
    [
        // Argentina
        new("AR_MERCOSUR_AUTO", Pais.AR, "LLNNNLL"), // AB123CD
        new("AR_VIEJA_AUTO", Pais.AR, "LLLNNN"), // ABC123
        new("AR_MERCOSUR_MOTO", Pais.AR, "LNNNLLL"), // A123BCD
        new("AR_VIEJA_MOTO", Pais.AR, "NNNLLL"), // 123ABC

        // México
        new("MX_AUTO_6", Pais.MX, "LLLNNN"), // ABC123
        new("MX_AUTO_7", Pais.MX, "LLLNNNN"), // ABC1234
        new("MX_AUTO_7L", Pais.MX, "LLLNNNL"), // ABC123D
        new("MX_CAMION", Pais.MX, "LLNNNNL"), // AB1234C
        new("MX_MOTO", Pais.MX, "LNNNLL"), // Y123AB
    ];

    public static ResultadoPatente Normalizar(string leida, Pais pais)
    {
        ArgumentNullException.ThrowIfNull(leida);

        var limpia = Limpiar(leida);
        var resultado = new ResultadoPatente { Leida = leida, Limpia = limpia ?? string.Empty, Pais = pais };
        if (string.IsNullOrEmpty(limpia))
        {
            return resultado;
        }

        var candidatos = new List<(FormatoPatente Formato, string Texto, List<Correccion> Correcciones)>();
        foreach (var formato in Formatos)
        {
            if (formato.Pais != pais || formato.Mascara.Length != limpia.Length)
            {
                continue;
            }

            if (Encajar(limpia, formato.Mascara) is { } encaje)
            {
                candidatos.Add((formato, encaje.Texto, encaje.Correcciones));
            }
        }

        if (candidatos.Count == 0)
        {
            return resultado;
        }

        var minimo = candidatos.Min(c => c.Correcciones.Count);
        var mejores = candidatos.Where(c => c.Correcciones.Count == minimo).ToList();
        if (mejores.Count > 1)
        {
            return resultado with { Ambigua = true };
        }

        var mejor = mejores[0];
        return resultado with
        {
            Normalizada = mejor.Texto,
            Formato = mejor.Formato,
            Correcciones = mejor.Correcciones,
        };
    }

    /// <summary>Mayúsculas, sin espacios, guiones ni puntos. Null si queda algún carácter no permitido.</summary>
    private static string? Limpiar(string leida)
    {
        var sb = new StringBuilder(leida.Length);
        foreach (var c in leida.Trim().ToUpperInvariant())
        {
            if (c is ' ' or '-' or '.')
            {
                continue;
            }

            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                sb.Append(c);
                continue;
            }

            // Ñ, letras acentuadas, símbolos: ningún formato los admite.
            return null;
        }

        return sb.ToString();
    }

    private static (string Texto, List<Correccion> Correcciones)? Encajar(string limpia, string mascara)
    {
        var texto = new char[limpia.Length];
        var correcciones = new List<Correccion>();

        for (var i = 0; i < limpia.Length; i++)
        {
            var c = limpia[i];
            var esLetra = mascara[i] == 'L';

            if (esLetra && char.IsAsciiLetterUpper(c) || !esLetra && char.IsAsciiDigit(c))
            {
                texto[i] = c;
                continue;
            }

            char? reemplazo = null;
            foreach (var (letra, digito) in Parecidos)
            {
                if (esLetra && c == digito)
                {
                    reemplazo = letra;
                }
                else if (!esLetra && c == letra)
                {
                    reemplazo = digito;
                }
            }

            if (reemplazo is null)
            {
                return null;
            }

            texto[i] = reemplazo.Value;
            correcciones.Add(new Correccion(i, c, reemplazo.Value));
        }

        return (new string(texto), correcciones);
    }
}
