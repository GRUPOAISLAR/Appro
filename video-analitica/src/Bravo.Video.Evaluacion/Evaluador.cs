using System.Globalization;
using System.Text;
using Bravo.Video.Patentes;

namespace Bravo.Video.Evaluacion;

public enum ResultadoPasada
{
    Correcta,

    /// <summary>El motor no generó evento para la pasada.</summary>
    NoDetectada,

    /// <summary>Hubo evento pero sin patente.</summary>
    SinLectura,

    /// <summary>Leyó algo que no encaja en ningún formato.</summary>
    Invalida,

    /// <summary>Leyó una patente válida pero distinta de la real.</summary>
    Incorrecta,
}

/// <summary>Porcentaje con su intervalo de confianza del 95% (Wilson).</summary>
public sealed record Proporcion(int Aciertos, int Total)
{
    private const double Z = 1.959963984540054;

    public double? Valor => Total == 0 ? null : (double)Aciertos / Total;

    /// <summary>
    /// Intervalo de Wilson, no el "normal" (p ± z·√(p(1−p)/n)): con porcentajes
    /// cerca de 100% y muestras de 100 a 200, el normal se pasa de 1 y subestima
    /// la incertidumbre justo donde se evalúa el criterio del 95%.
    /// </summary>
    public (double Desde, double Hasta)? Intervalo95
    {
        get
        {
            if (Total == 0)
            {
                return null;
            }

            double n = Total, p = (double)Aciertos / Total, z2 = Z * Z;
            var centro = (p + z2 / (2 * n)) / (1 + z2 / n);
            var margen = Z * Math.Sqrt(p * (1 - p) / n + z2 / (4 * n * n)) / (1 + z2 / n);
            return (Math.Max(0, centro - margen), Math.Min(1, centro + margen));
        }
    }
}

public sealed record ResumenPeriodo
{
    public required string Nombre { get; init; }

    /// <summary>Pasadas con patente legible a ojo: es la base del porcentaje.</summary>
    public required int Evaluadas { get; init; }

    public required IReadOnlyDictionary<ResultadoPasada, int> PorResultado { get; init; }

    /// <summary>Bien leídas sobre todas las evaluadas, detectadas o no. Es el número del criterio del 95%.</summary>
    public required Proporcion Lectura { get; init; }

    /// <summary>Bien leídas sobre las que el motor detectó: separa "no lo vio" de "lo vio y leyó mal".</summary>
    public required Proporcion LecturaSobreDetectadas { get; init; }

    /// <summary>De las bien leídas, cuántas necesitaron corrección por parecido.</summary>
    public required int CorregidasPorParecido { get; init; }
}

public sealed record ResumenConteo(int VehiculosReales, int EventosEnMuestra, int NoDetectados, int FalsosPositivos)
{
    /// <summary>(eventos − reales) / reales. Positivo: cuenta de más.</summary>
    public double? ErrorRelativo => VehiculosReales == 0 ? null : (double)(EventosEnMuestra - VehiculosReales) / VehiculosReales;
}

public sealed record Informe
{
    public required IReadOnlyList<ResumenPeriodo> Periodos { get; init; }
    public required ResumenConteo Conteo { get; init; }

    /// <summary>Confusiones de carácter más frecuentes ("real→leído"), sin patentes.</summary>
    public required IReadOnlyList<(string Confusion, int Veces)> ErroresComunes { get; init; }

    public required int Ilegibles { get; init; }
    public required int LecturasSinEtiquetar { get; init; }

    /// <summary>Problemas de los datos (etiquetas que apuntan a eventos inexistentes, etc.).</summary>
    public required IReadOnlyList<string> Problemas { get; init; }
}

/// <summary>
/// Cruza la planilla etiquetada a mano con las lecturas que llegaron a la ingesta.
///
/// Reglas, todas explícitas:
/// - Una pasada se evalúa si su patente real es legible a ojo (no "?" ni "-").
/// - Es correcta si la patente normalizada del evento es igual a la patente real
///   normalizada con las mismas reglas. Nada más cuenta como acierto.
/// - El porcentaje principal usa como base todas las pasadas evaluadas, aunque el
///   motor no las haya detectado: un auto que no se vio es una patente no leída.
/// - El conteo solo vale si la muestra cubre ventanas de tiempo completas
///   (todas las pasadas de esa franja, no una selección).
/// - Las lecturas que no aparecen en la planilla no se evalúan; solo se cuentan.
///
/// El informe no incluye patentes: solo cantidades y confusiones de carácter.
/// </summary>
public static class Evaluador
{
    public static Informe Evaluar(IReadOnlyList<Etiqueta> etiquetas, IReadOnlyList<FilaLectura> lecturas, Pais pais)
    {
        var problemas = new List<string>();
        var porId = new Dictionary<Guid, FilaLectura>();
        foreach (var l in lecturas)
        {
            if (!porId.TryAdd(l.EventId, l))
            {
                problemas.Add($"Lectura {l.EventId} repetida en la planilla de lecturas; se usa la primera.");
            }
        }

        var etiquetados = new HashSet<Guid>();
        var resultados = new List<(Etiqueta Etiqueta, ResultadoPasada Resultado, int Correcciones)>();
        var confusiones = new Dictionary<string, int>(StringComparer.Ordinal);
        var ilegibles = 0;
        var falsosPositivos = 0;
        var eventosEnMuestra = 0;
        var noDetectados = 0;
        var reales = 0;

        for (var i = 0; i < etiquetas.Count; i++)
        {
            var etiqueta = etiquetas[i];
            FilaLectura? lectura = null;
            if (etiqueta.EventId is { } id)
            {
                if (!etiquetados.Add(id))
                {
                    problemas.Add($"Etiqueta fila {i + 2}: el evento {id} ya estaba etiquetado; se ignora.");
                    continue;
                }

                if (!porId.TryGetValue(id, out lectura))
                {
                    problemas.Add($"Etiqueta fila {i + 2}: el evento {id} no está en las lecturas; se ignora.");
                    continue;
                }

                eventosEnMuestra++;
            }

            if (etiqueta.PatenteReal == Etiqueta.SinVehiculo)
            {
                if (lectura is null)
                {
                    problemas.Add($"Etiqueta fila {i + 2}: marcada sin vehículo pero sin evento; se ignora.");
                }
                else
                {
                    falsosPositivos++;
                }

                continue;
            }

            reales++;
            if (lectura is null)
            {
                noDetectados++;
            }

            if (etiqueta.PatenteReal == Etiqueta.Ilegible)
            {
                ilegibles++;
                continue;
            }

            var real = NormalizadorPatentes.Normalizar(etiqueta.PatenteReal, pais);
            if (!real.Valida)
            {
                problemas.Add($"Etiqueta fila {i + 2}: la patente real no tiene un formato válido; se ignora.");
                reales--;
                if (lectura is null)
                {
                    noDetectados--;
                }

                continue;
            }

            var (resultado, correcciones) = Clasificar(lectura, real.Normalizada!, pais);
            resultados.Add((etiqueta, resultado, correcciones));

            if (resultado is ResultadoPasada.Incorrecta or ResultadoPasada.Invalida && lectura?.PatenteLeida is { } leida)
            {
                RegistrarConfusiones(confusiones, real.Normalizada!, Limpiar(leida));
            }
        }

        var sinEtiquetar = lecturas.Count(l => !etiquetados.Contains(l.EventId));

        return new Informe
        {
            Periodos =
            [
                Resumir("Día", resultados.Where(r => r.Etiqueta.Periodo == Periodo.Dia).ToList()),
                Resumir("Noche", resultados.Where(r => r.Etiqueta.Periodo == Periodo.Noche).ToList()),
                Resumir("Total", resultados),
            ],
            Conteo = new ResumenConteo(reales, eventosEnMuestra, noDetectados, falsosPositivos),
            ErroresComunes = confusiones
                .OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal)
                .Take(10)
                .Select(c => (c.Key, c.Value))
                .ToList(),
            Ilegibles = ilegibles,
            LecturasSinEtiquetar = sinEtiquetar,
            Problemas = problemas,
        };
    }

    private static (ResultadoPasada, int) Clasificar(FilaLectura? lectura, string real, Pais pais)
    {
        if (lectura is null)
        {
            return (ResultadoPasada.NoDetectada, 0);
        }

        if (lectura.PatenteLeida is null)
        {
            return (ResultadoPasada.SinLectura, 0);
        }

        // Se recalcula desde lo leído con las reglas actuales, no se confía en la
        // columna normalizada: así el evaluador mide el normalizador de hoy.
        var leida = NormalizadorPatentes.Normalizar(lectura.PatenteLeida, pais);
        if (!leida.Valida)
        {
            return (ResultadoPasada.Invalida, 0);
        }

        return leida.Normalizada == real
            ? (ResultadoPasada.Correcta, leida.Correcciones.Count)
            : (ResultadoPasada.Incorrecta, 0);
    }

    private static ResumenPeriodo Resumir(string nombre, List<(Etiqueta Etiqueta, ResultadoPasada Resultado, int Correcciones)> r)
    {
        var porResultado = Enum.GetValues<ResultadoPasada>().ToDictionary(x => x, x => r.Count(y => y.Resultado == x));
        var correctas = porResultado[ResultadoPasada.Correcta];
        return new ResumenPeriodo
        {
            Nombre = nombre,
            Evaluadas = r.Count,
            PorResultado = porResultado,
            Lectura = new Proporcion(correctas, r.Count),
            LecturaSobreDetectadas = new Proporcion(correctas, r.Count - porResultado[ResultadoPasada.NoDetectada]),
            CorregidasPorParecido = r.Count(x => x.Resultado == ResultadoPasada.Correcta && x.Correcciones > 0),
        };
    }

    /// <summary>Solo si tienen el mismo largo: carácter por carácter, "real→leído".</summary>
    private static void RegistrarConfusiones(Dictionary<string, int> confusiones, string real, string leida)
    {
        if (real.Length != leida.Length)
        {
            confusiones["largo distinto"] = confusiones.GetValueOrDefault("largo distinto") + 1;
            return;
        }

        for (var i = 0; i < real.Length; i++)
        {
            if (real[i] != leida[i])
            {
                var clave = $"{real[i]}→{leida[i]}";
                confusiones[clave] = confusiones.GetValueOrDefault(clave) + 1;
            }
        }
    }

    private static string Limpiar(string s) =>
        new(s.ToUpperInvariant().Where(c => c is not (' ' or '-' or '.')).ToArray());

    /// <summary>Informe en Markdown. Sin patentes.</summary>
    public static string ComoMarkdown(Informe informe, decimal criterio = 0.95m)
    {
        var c = CultureInfo.GetCultureInfo("es-AR");
        string Pct(double? v) => v is null ? "—" : (v.Value * 100).ToString("0.0", c) + " %";

        var sb = new StringBuilder();
        sb.AppendLine("# Evaluación de lectura de patentes").AppendLine();
        sb.AppendLine("## Lectura").AppendLine();
        sb.AppendLine("| Período | Evaluadas | Correctas | % (IC 95 %) | % sobre detectadas | No detectadas | Sin lectura | Inválidas | Incorrectas | Corregidas por parecido |");
        sb.AppendLine("| --- | ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var p in informe.Periodos)
        {
            var ic = p.Lectura.Intervalo95 is { } i ? $" ({Pct(i.Desde)} – {Pct(i.Hasta)})" : string.Empty;
            sb.AppendLine($"| {p.Nombre} | {p.Evaluadas} | {p.PorResultado[ResultadoPasada.Correcta]} | {Pct(p.Lectura.Valor)}{ic} | " +
                          $"{Pct(p.LecturaSobreDetectadas.Valor)} | {p.PorResultado[ResultadoPasada.NoDetectada]} | " +
                          $"{p.PorResultado[ResultadoPasada.SinLectura]} | {p.PorResultado[ResultadoPasada.Invalida]} | " +
                          $"{p.PorResultado[ResultadoPasada.Incorrecta]} | {p.CorregidasPorParecido} |");
        }

        sb.AppendLine();
        var criterioDouble = (double)criterio;
        foreach (var p in informe.Periodos)
        {
            if (p.Lectura.Intervalo95 is not { } i)
            {
                continue;
            }

            var veredicto = i.Desde >= criterioDouble ? "cumple"
                : i.Hasta < criterioDouble ? "no cumple"
                : "no se puede afirmar con esta muestra (el intervalo cruza el criterio)";
            sb.AppendLine($"- **{p.Nombre}**: criterio {Pct(criterioDouble)} → {veredicto}.");
        }

        sb.AppendLine().AppendLine("## Conteo").AppendLine();
        var k = informe.Conteo;
        sb.AppendLine($"- Vehículos reales en la muestra: {k.VehiculosReales}");
        sb.AppendLine($"- Eventos en la muestra: {k.EventosEnMuestra} (falsos positivos: {k.FalsosPositivos}; no detectados: {k.NoDetectados})");
        sb.AppendLine($"- Error de conteo: {Pct(k.ErrorRelativo)} (válido solo si la muestra cubre franjas horarias completas)");

        sb.AppendLine().AppendLine("## Confusiones más comunes (real → leído)").AppendLine();
        if (informe.ErroresComunes.Count == 0)
        {
            sb.AppendLine("Ninguna.");
        }

        foreach (var (confusion, veces) in informe.ErroresComunes)
        {
            sb.AppendLine($"- {confusion}: {veces}");
        }

        sb.AppendLine().AppendLine("## Datos").AppendLine();
        sb.AppendLine($"- Pasadas ilegibles a ojo (excluidas del porcentaje): {informe.Ilegibles}");
        sb.AppendLine($"- Lecturas fuera de la muestra (no evaluadas): {informe.LecturasSinEtiquetar}");
        sb.AppendLine($"- Problemas en las planillas: {informe.Problemas.Count}");
        foreach (var problema in informe.Problemas.Take(20))
        {
            sb.AppendLine($"  - {problema}");
        }

        return sb.ToString();
    }
}
