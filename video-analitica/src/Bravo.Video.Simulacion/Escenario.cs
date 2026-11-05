using Bravo.Video.Evaluacion;
using Bravo.Video.Patentes;

namespace Bravo.Video.Simulacion;

/// <summary>
/// Probabilidad de cada falla del motor en una pasada. Se aplican en este orden y
/// son excluyentes: la suma tiene que ser menor o igual a 1.
/// </summary>
/// <param name="SinDeteccion">El motor no vio el vehículo.</param>
/// <param name="SinPatente">Vio el vehículo pero no leyó la patente.</param>
/// <param name="ErrorParecido">Confundió un carácter por uno parecido (O/0, I/1…): el normalizador puede corregirlo.</param>
/// <param name="ErrorOtro">Confundió un carácter por otro sin parecido: el normalizador no puede corregirlo.</param>
public sealed record RuidoLectura(double SinDeteccion, double SinPatente, double ErrorParecido, double ErrorOtro)
{
    public static readonly RuidoLectura DiaPorDefecto = new(0.02, 0.03, 0.04, 0.02);
    public static readonly RuidoLectura NochePorDefecto = new(0.05, 0.10, 0.10, 0.05);
    public static readonly RuidoLectura Ninguno = new(0, 0, 0, 0);

    public double Total => SinDeteccion + SinPatente + ErrorParecido + ErrorOtro;
}

public sealed record ParametrosSimulacion
{
    /// <summary>Misma semilla, mismo escenario: las pruebas y las demos se pueden repetir.</summary>
    public int Semilla { get; init; } = 1;

    /// <summary>Medianoche del primer día, en la zona de la sede.</summary>
    public required DateTimeOffset Inicio { get; init; }

    public int Dias { get; init; } = 1;
    public int AutosPorDia { get; init; } = 200;
    public Pais Pais { get; init; } = Pais.AR;

    /// <summary>Fracción de autos que llegan de noche (entre <see cref="HoraNocheDesde"/> y <see cref="HoraNocheHasta"/>).</summary>
    public double FraccionNoche { get; init; } = 0.25;

    public int HoraNocheDesde { get; init; } = 20;
    public int HoraNocheHasta { get; init; } = 7;
    public TimeSpan EstadiaMinima { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan EstadiaMaxima { get; init; } = TimeSpan.FromHours(4);
    public RuidoLectura RuidoDia { get; init; } = RuidoLectura.DiaPorDefecto;
    public RuidoLectura RuidoNoche { get; init; } = RuidoLectura.NochePorDefecto;
}

/// <summary>
/// Un vehículo cruzando la cámara: lo que pasó de verdad y lo que "leyó" el motor.
/// </summary>
public sealed record Pasada
{
    public required int Numero { get; init; }

    /// <summary>Identificador estable de la pasada, ej. "000012i" (ingreso del auto 12).</summary>
    public string Clave => $"{Numero:D6}{(EsIngreso ? 'i' : 'e')}";

    public required string PatenteReal { get; init; }
    public required bool EsIngreso { get; init; }
    public required DateTimeOffset Ts { get; init; }
    public required Periodo Periodo { get; init; }
    public required bool Detectada { get; init; }

    /// <summary>Lo que devolvería el motor. Null si no detectó o no leyó.</summary>
    public string? PatenteLeida { get; init; }

    public decimal? Confianza { get; init; }
}

/// <summary>
/// Genera autos que entran, estacionan y salen, con patentes inventadas y fallas
/// de lectura según el período.
///
/// Las patentes empiezan con Z (AR: "ZV".."ZZ", MX: "Z" + dos letras). En
/// Argentina esa serie del Mercosur no se va a emitir en décadas; en México no se
/// puede garantizar, pero es improbable que coincida con una real. Nunca usar
/// patentes reales en simulaciones.
/// </summary>
public static class GeneradorEscenario
{
    private const string Letras = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string SegundaLetraAr = "VWXYZ";

    public static IReadOnlyList<Pasada> Generar(ParametrosSimulacion p)
    {
        if (p.RuidoDia.Total > 1 || p.RuidoNoche.Total > 1)
        {
            throw new ArgumentException("La suma de probabilidades de ruido no puede superar 1.");
        }

        var azar = new Random(p.Semilla);
        var usadas = new HashSet<string>(StringComparer.Ordinal);
        var pasadas = new List<Pasada>();
        var numero = 0;

        for (var dia = 0; dia < p.Dias; dia++)
        {
            var medianoche = p.Inicio.AddDays(dia);
            for (var i = 0; i < p.AutosPorDia; i++)
            {
                numero++;
                var patente = PatenteNueva(azar, p.Pais, usadas);
                var llegada = medianoche + HoraDeLlegada(azar, p);
                var estadia = p.EstadiaMinima + TimeSpan.FromTicks((long)(azar.NextDouble() * (p.EstadiaMaxima - p.EstadiaMinima).Ticks));

                // Redondeo al milisegundo: es la resolución del contrato y de Frigate.
                llegada = Milisegundo(llegada);
                var salida = Milisegundo(llegada + estadia);

                pasadas.Add(Pasar(azar, p, numero, patente, esIngreso: true, llegada));
                pasadas.Add(Pasar(azar, p, numero, patente, esIngreso: false, salida));
            }
        }

        return pasadas.OrderBy(x => x.Ts).ThenBy(x => x.Clave, StringComparer.Ordinal).ToList();
    }

    public static Periodo PeriodoDe(DateTimeOffset ts, ParametrosSimulacion p) =>
        ts.Hour >= p.HoraNocheDesde || ts.Hour < p.HoraNocheHasta ? Periodo.Noche : Periodo.Dia;

    private static Pasada Pasar(Random azar, ParametrosSimulacion p, int numero, string patente, bool esIngreso, DateTimeOffset ts)
    {
        var periodo = PeriodoDe(ts, p);
        var ruido = periodo == Periodo.Dia ? p.RuidoDia : p.RuidoNoche;
        var r = azar.NextDouble();
        var pasada = new Pasada
        {
            Numero = numero, PatenteReal = patente, EsIngreso = esIngreso, Ts = ts, Periodo = periodo, Detectada = true,
        };

        if (r < ruido.SinDeteccion)
        {
            return pasada with { Detectada = false };
        }

        r -= ruido.SinDeteccion;
        if (r < ruido.SinPatente)
        {
            return pasada;
        }

        r -= ruido.SinPatente;
        string leida;
        if (r < ruido.ErrorParecido)
        {
            leida = CambiarPorParecido(azar, patente) ?? CambiarPorOtro(azar, patente);
        }
        else if (r - ruido.ErrorParecido < ruido.ErrorOtro)
        {
            leida = CambiarPorOtro(azar, patente);
        }
        else
        {
            return pasada with { PatenteLeida = patente, Confianza = Confianza(azar, 0.85, 0.99) };
        }

        return pasada with { PatenteLeida = leida, Confianza = Confianza(azar, 0.60, 0.90) };
    }

    private static string PatenteNueva(Random azar, Pais pais, HashSet<string> usadas)
    {
        while (true)
        {
            var patente = pais == Pais.AR
                ? $"Z{SegundaLetraAr[azar.Next(SegundaLetraAr.Length)]}{azar.Next(1000):D3}{Letra(azar)}{Letra(azar)}"
                : $"Z{Letra(azar)}{Letra(azar)}{azar.Next(1000):D3}";
            if (usadas.Add(patente))
            {
                return patente;
            }
        }
    }

    private static TimeSpan HoraDeLlegada(Random azar, ParametrosSimulacion p)
    {
        // De día, entre el fin de la noche y su comienzo; de noche, el resto, dando la vuelta a las 24.
        var horasDia = p.HoraNocheDesde - p.HoraNocheHasta;
        if (azar.NextDouble() >= p.FraccionNoche)
        {
            return TimeSpan.FromHours(p.HoraNocheHasta + azar.NextDouble() * horasDia);
        }

        var hora = p.HoraNocheDesde + azar.NextDouble() * (24 - horasDia);
        return TimeSpan.FromHours(hora >= 24 ? hora - 24 : hora);
    }

    /// <summary>Cambia un carácter que tenga parecido (ej. O→0). Null si la patente no tiene ninguno.</summary>
    private static string? CambiarPorParecido(Random azar, string patente)
    {
        var posiciones = Enumerable.Range(0, patente.Length)
            .Where(i => NormalizadorPatentes.Parecidos.Any(x => x.Letra == patente[i] || x.Digito == patente[i]))
            .ToList();
        if (posiciones.Count == 0)
        {
            return null;
        }

        var pos = posiciones[azar.Next(posiciones.Count)];
        var par = NormalizadorPatentes.Parecidos.First(x => x.Letra == patente[pos] || x.Digito == patente[pos]);
        var nuevo = patente[pos] == par.Letra ? par.Digito : par.Letra;
        return Reemplazar(patente, pos, nuevo);
    }

    /// <summary>Cambia un carácter por otro de la misma clase que no sea su parecido: queda una patente válida pero equivocada.</summary>
    private static string CambiarPorOtro(Random azar, string patente)
    {
        var pos = azar.Next(patente.Length);
        var actual = patente[pos];
        var alfabeto = char.IsAsciiDigit(actual) ? "0123456789" : Letras;
        char nuevo;
        do
        {
            nuevo = alfabeto[azar.Next(alfabeto.Length)];
        }
        while (nuevo == actual);

        return Reemplazar(patente, pos, nuevo);
    }

    private static string Reemplazar(string s, int pos, char c) => string.Concat(s.AsSpan(0, pos), c.ToString(), s.AsSpan(pos + 1));

    private static char Letra(Random azar) => Letras[azar.Next(Letras.Length)];

    private static decimal Confianza(Random azar, double min, double max) =>
        decimal.Round((decimal)(min + azar.NextDouble() * (max - min)), 2);

    private static DateTimeOffset Milisegundo(DateTimeOffset ts) =>
        new(ts.Ticks - ts.Ticks % TimeSpan.TicksPerMillisecond, ts.Offset);
}
