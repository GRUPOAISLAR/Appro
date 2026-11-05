using System.Globalization;
using System.Text;

namespace Bravo.Video.Evaluacion;

/// <summary>
/// CSV mínimo (RFC 4180): coma, comillas dobles para campos con coma, comilla o
/// salto de línea, UTF-8 sin BOM. Lo comparten el simulador y el evaluador.
/// </summary>
public static class Csv
{
    public static string Linea(params string?[] campos) => string.Join(',', campos.Select(Campo));

    public static string Campo(string? valor)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return string.Empty;
        }

        return valor.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + valor.Replace("\"", "\"\"") + "\""
            : valor;
    }

    public static IEnumerable<string[]> Leer(TextReader lector)
    {
        var campos = new List<string>();
        var actual = new StringBuilder();
        var entreComillas = false;
        int c;
        while ((c = lector.Read()) != -1)
        {
            var ch = (char)c;
            if (entreComillas)
            {
                if (ch == '"')
                {
                    if (lector.Peek() == '"')
                    {
                        actual.Append('"');
                        lector.Read();
                    }
                    else
                    {
                        entreComillas = false;
                    }
                }
                else
                {
                    actual.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    entreComillas = true;
                    break;
                case ',':
                    campos.Add(actual.ToString());
                    actual.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    campos.Add(actual.ToString());
                    actual.Clear();
                    yield return campos.ToArray();
                    campos.Clear();
                    break;
                default:
                    actual.Append(ch);
                    break;
            }
        }

        if (actual.Length > 0 || campos.Count > 0)
        {
            campos.Add(actual.ToString());
            yield return campos.ToArray();
        }
    }

    public static string Fecha(DateTimeOffset ts) => ts.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
}
