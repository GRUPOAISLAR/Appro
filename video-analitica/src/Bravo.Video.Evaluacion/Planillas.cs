using System.Globalization;
using Bravo.Video.Contratos;

namespace Bravo.Video.Evaluacion;

/// <summary>Una lectura tal como llegó a la ingesta, en el formato que consume el evaluador.</summary>
public sealed record FilaLectura(
    Guid EventId,
    DateTimeOffset Ts,
    string Tipo,
    string Camara,
    string? PatenteLeida,
    string? PatenteNormalizada,
    bool Valida,
    decimal? Confianza)
{
    public static FilaLectura De(EventoVehiculo e) => new(
        e.EventId, e.Ts, TiposEvento.De(e), e.Camara ?? string.Empty,
        e.Patente?.Leida, e.Patente?.Normalizada, e.Patente?.Valida ?? false, e.Patente?.Confianza);
}

/// <summary>
/// Formatos de las dos planillas. Son el contrato entre el etiquetado a mano, la
/// exportación de la ingesta y el evaluador:
///
/// <c>etiquetas.csv</c>: <c>eventId,ts,patenteReal,periodo</c>
/// <c>lecturas.csv</c>: <c>eventId,ts,tipo,camara,patenteLeida,patenteNormalizada,valida,confianza</c>
///
/// Con patentes reales, estas planillas son datos personales: archivo privado de
/// quien hace la evaluación, nunca en el repo ni en un documento compartido.
/// </summary>
public static class Planillas
{
    public static readonly string[] EncabezadoEtiquetas = ["eventId", "ts", "patenteReal", "periodo"];

    public static readonly string[] EncabezadoLecturas =
        ["eventId", "ts", "tipo", "camara", "patenteLeida", "patenteNormalizada", "valida", "confianza"];

    public static void EscribirEtiquetas(TextWriter salida, IEnumerable<Etiqueta> etiquetas)
    {
        salida.Write(Csv.Linea(EncabezadoEtiquetas) + "\n");
        foreach (var e in etiquetas)
        {
            salida.Write(Csv.Linea(e.EventId?.ToString(), Csv.Fecha(e.Ts), e.PatenteReal, e.Periodo.ToString().ToUpperInvariant()) + "\n");
        }
    }

    public static void EscribirLecturas(TextWriter salida, IEnumerable<FilaLectura> lecturas)
    {
        salida.Write(Csv.Linea(EncabezadoLecturas) + "\n");
        foreach (var l in lecturas)
        {
            salida.Write(Csv.Linea(
                l.EventId.ToString(), Csv.Fecha(l.Ts), l.Tipo, l.Camara, l.PatenteLeida, l.PatenteNormalizada,
                l.Valida ? "true" : "false", l.Confianza?.ToString(CultureInfo.InvariantCulture)) + "\n");
        }
    }

    public static IReadOnlyList<Etiqueta> LeerEtiquetas(TextReader entrada)
    {
        var filas = Filas(entrada, EncabezadoEtiquetas);
        return filas.Select((f, i) => new Etiqueta(
                string.IsNullOrWhiteSpace(f[0]) ? null : Guid.Parse(f[0]),
                Fecha(f[1], i),
                f[2].Trim(),
                f[3].Trim().ToUpperInvariant() switch
                {
                    "DIA" or "DÍA" => Periodo.Dia,
                    "NOCHE" => Periodo.Noche,
                    _ => throw new FormatException($"Fila {i + 2}: periodo tiene que ser DIA o NOCHE."),
                }))
            .ToList();
    }

    public static IReadOnlyList<FilaLectura> LeerLecturas(TextReader entrada)
    {
        var filas = Filas(entrada, EncabezadoLecturas);
        return filas.Select((f, i) => new FilaLectura(
                Guid.Parse(f[0]),
                Fecha(f[1], i),
                f[2],
                f[3],
                Vacio(f[4]),
                Vacio(f[5]),
                f[6].Trim().Equals("true", StringComparison.OrdinalIgnoreCase),
                string.IsNullOrWhiteSpace(f[7]) ? null : decimal.Parse(f[7], CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static List<string[]> Filas(TextReader entrada, string[] encabezado)
    {
        var filas = Csv.Leer(entrada).Where(f => f.Length > 1 || f[0].Length > 0).ToList();
        if (filas.Count == 0 || !filas[0].Select(x => x.Trim()).SequenceEqual(encabezado))
        {
            throw new FormatException($"Encabezado esperado: {string.Join(',', encabezado)}");
        }

        for (var i = 1; i < filas.Count; i++)
        {
            if (filas[i].Length != encabezado.Length)
            {
                throw new FormatException($"Fila {i + 1}: tiene {filas[i].Length} columnas y se esperan {encabezado.Length}.");
            }
        }

        return filas.Skip(1).ToList();
    }

    private static DateTimeOffset Fecha(string texto, int indice)
    {
        if (!DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
        {
            throw new FormatException($"Fila {indice + 2}: fecha inválida.");
        }

        return ts;
    }

    private static string? Vacio(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
