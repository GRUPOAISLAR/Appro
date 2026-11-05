using Bravo.Video.Evaluacion;
using Bravo.Video.Lectores;
using Bravo.Video.Patentes;
using Bravo.Video.Simulacion;

namespace Bravo.Video.Herramientas.Tests;

// Patentes inventadas con prefijo Z.
public class EvaluacionPruebas
{
    private static readonly DateTimeOffset T = new(2026, 11, 10, 12, 0, 0, TimeSpan.FromHours(-3));
    private static int _n;

    private static Guid Id() => new($"00000000-0000-0000-0000-{Interlocked.Increment(ref _n):D12}");

    private static FilaLectura Lectura(Guid id, string? leida) =>
        new(id, T, "VEHICULO_INGRESO", "porton", leida, null, false, 0.9m);

    [Fact]
    public void Clasifica_cada_caso()
    {
        var (correcta, corregida, sinLectura, invalida, incorrecta, falsoPositivo, ilegible) = (Id(), Id(), Id(), Id(), Id(), Id(), Id());
        var etiquetas = new List<Etiqueta>
        {
            new(correcta, T, "ZV100AA", Periodo.Dia),
            new(corregida, T, "ZV100AB", Periodo.Dia),
            new(sinLectura, T, "ZV100AC", Periodo.Dia),
            new(invalida, T, "ZV100AD", Periodo.Noche),
            new(incorrecta, T, "ZV100AE", Periodo.Noche),
            new(null, T, "ZV100AF", Periodo.Noche), // no detectada
            new(falsoPositivo, T, Etiqueta.SinVehiculo, Periodo.Dia),
            new(ilegible, T, Etiqueta.Ilegible, Periodo.Noche),
        };
        var lecturas = new List<FilaLectura>
        {
            Lectura(correcta, "ZV100AA"),
            Lectura(corregida, "ZV1O0AB"), // O→0 corregible
            Lectura(sinLectura, null),
            Lectura(invalida, "ZV10AD"),
            Lectura(incorrecta, "ZV100AX"),
            Lectura(falsoPositivo, "ZV999ZZ"),
            Lectura(ilegible, null),
            Lectura(Id(), "ZW123AA"), // fuera de la muestra
        };

        var informe = Evaluador.Evaluar(etiquetas, lecturas, Pais.AR);
        var dia = informe.Periodos[0];
        var noche = informe.Periodos[1];
        var total = informe.Periodos[2];

        Assert.Equal((3, 2, 1), (dia.Evaluadas, dia.PorResultado[ResultadoPasada.Correcta], dia.PorResultado[ResultadoPasada.SinLectura]));
        Assert.Equal(1, dia.CorregidasPorParecido);
        Assert.Equal(1, noche.PorResultado[ResultadoPasada.Invalida]);
        Assert.Equal(1, noche.PorResultado[ResultadoPasada.Incorrecta]);
        Assert.Equal(1, noche.PorResultado[ResultadoPasada.NoDetectada]);
        Assert.Equal(new Proporcion(2, 6), total.Lectura);
        Assert.Equal(new Proporcion(2, 5), total.LecturaSobreDetectadas);

        Assert.Equal(new ResumenConteo(VehiculosReales: 7, EventosEnMuestra: 7, NoDetectados: 1, FalsosPositivos: 1), informe.Conteo);
        Assert.Equal(1, informe.Ilegibles);
        Assert.Equal(1, informe.LecturasSinEtiquetar);
        Assert.Contains(("E→X", 1), informe.ErroresComunes);
        Assert.Contains(("largo distinto", 1), informe.ErroresComunes);
        Assert.Empty(informe.Problemas);
    }

    [Fact]
    public void Reporta_problemas_de_las_planillas_sin_frenar()
    {
        var a = Id();
        var etiquetas = new List<Etiqueta>
        {
            new(a, T, "ZV100AA", Periodo.Dia),
            new(a, T, "ZV100AA", Periodo.Dia), // repetida
            new(Id(), T, "ZV100AB", Periodo.Dia), // evento inexistente
            new(null, T, "patente rara", Periodo.Dia), // formato inválido
        };

        var informe = Evaluador.Evaluar(etiquetas, [Lectura(a, "ZV100AA")], Pais.AR);

        Assert.Equal(3, informe.Problemas.Count);
        Assert.Equal(new Proporcion(1, 1), informe.Periodos[2].Lectura);
        Assert.Equal(1, informe.Conteo.VehiculosReales);
    }

    [Fact]
    public void Intervalo_de_Wilson()
    {
        // Valor de referencia: 95 de 100 → [0,8882; 0,9785].
        var (desde, hasta) = new Proporcion(95, 100).Intervalo95!.Value;
        Assert.Equal(0.8882, desde, 3);
        Assert.Equal(0.9785, hasta, 3);

        var (_, techo) = new Proporcion(200, 200).Intervalo95!.Value;
        Assert.Equal(1.0, techo);
        Assert.Null(new Proporcion(0, 0).Intervalo95);
    }

    [Theory]
    [InlineData(199, 200, "cumple")]
    [InlineData(150, 200, "no cumple")]
    [InlineData(190, 200, "no se puede afirmar")]
    public void Veredicto_segun_el_intervalo(int aciertos, int total, string esperado)
    {
        var etiquetas = new List<Etiqueta>();
        var lecturas = new List<FilaLectura>();
        for (var i = 0; i < total; i++)
        {
            var id = Id();
            var patente = $"ZV{i:D3}AA";
            etiquetas.Add(new Etiqueta(id, T, patente, Periodo.Dia));
            lecturas.Add(Lectura(id, i < aciertos ? patente : null));
        }

        var markdown = Evaluador.ComoMarkdown(Evaluador.Evaluar(etiquetas, lecturas, Pais.AR));
        var linea = markdown.Split('\n').Single(l => l.StartsWith("- **Día**"));
        Assert.Contains(esperado, linea);
        Assert.DoesNotContain("ZV0", markdown); // el informe no lleva patentes
    }

    [Fact]
    public void Simulacion_seca_y_evaluacion_de_punta_a_punta()
    {
        var pasadas = GeneradorEscenario.Generar(SimulacionPruebas.Parametros(autos: 200));
        var instalacion = SimulacionPruebas.Instalacion(Motores.Frigate);
        var etiquetas = Verdad.Etiquetas(pasadas, instalacion);
        var lecturas = SimulacionSeca.Ejecutar(pasadas, instalacion).Select(FilaLectura.De).ToList();

        var informe = Evaluador.Evaluar(etiquetas, lecturas, Pais.AR);

        Assert.Empty(informe.Problemas);
        Assert.Equal(0, informe.LecturasSinEtiquetar);
        Assert.Equal(pasadas.Count(p => !p.Detectada), informe.Conteo.NoDetectados);

        // Lo que el evaluador cuenta como correcto tiene que coincidir con la verdad del simulador.
        var esperadas = pasadas.Count(p => p.PatenteLeida is { } l
            && NormalizadorPatentes.Normalizar(l, Pais.AR).Normalizada == p.PatenteReal);
        Assert.Equal(esperadas, informe.Periodos[2].PorResultado[ResultadoPasada.Correcta]);
    }
}

public class PlanillasPruebas
{
    [Fact]
    public void Ida_y_vuelta_y_comillas()
    {
        var etiquetas = new List<Etiqueta>
        {
            new(Guid.NewGuid(), new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.FromHours(-3)), "ZV100AA", Periodo.Dia),
            new(null, new DateTimeOffset(2026, 11, 10, 22, 0, 0, TimeSpan.FromHours(-3)), Etiqueta.Ilegible, Periodo.Noche),
        };
        var escritura = new StringWriter();
        Planillas.EscribirEtiquetas(escritura, etiquetas);
        Assert.Equal(etiquetas, Planillas.LeerEtiquetas(new StringReader(escritura.ToString())));

        var lectura = new FilaLectura(Guid.NewGuid(), etiquetas[0].Ts, "VEHICULO_INGRESO", "cámara, \"norte\"", "ZV 100-AA", "ZV100AA", true, 0.91m);
        var l = new StringWriter();
        Planillas.EscribirLecturas(l, [lectura]);
        Assert.Equal(lectura, Planillas.LeerLecturas(new StringReader(l.ToString())).Single());
    }

    [Theory]
    [InlineData("otra,cosa\n")]
    [InlineData("eventId,ts,patenteReal,periodo\n,2026-11-10T08:00:00-03:00,ZV100AA\n")]
    [InlineData("eventId,ts,patenteReal,periodo\n,2026-11-10T08:00:00-03:00,ZV100AA,TARDE\n")]
    [InlineData("eventId,ts,patenteReal,periodo\n,2026-11-10T08:00:00,ZV100AA,DIA\n")] // sin zona
    public void Rechaza_planillas_mal_armadas(string csv)
    {
        Assert.Throws<FormatException>(() => Planillas.LeerEtiquetas(new StringReader(csv)));
    }
}

public class LineaDeComandosPruebas
{
    [Fact]
    public async Task Simular_en_seco_y_evaluar()
    {
        var dir = Directory.CreateTempSubdirectory("video-herramientas-").FullName;
        try
        {
            var salida = new StringWriter();
            Assert.Equal(0, await Program.SimularAsync(new() { ["modo"] = "seco", ["autos"] = "50", ["salida"] = dir }, salida));
            Assert.Contains("100 pasadas simuladas", salida.ToString());

            var informe = Path.Combine(dir, "informe.md");
            var texto = new StringWriter();
            Assert.Equal(0, Program.Evaluar(new()
            {
                ["etiquetas"] = Path.Combine(dir, "etiquetas.csv"),
                ["lecturas"] = Path.Combine(dir, "lecturas.csv"),
                ["informe"] = informe,
            }, texto));

            Assert.StartsWith("# Evaluación de lectura de patentes", File.ReadAllText(informe));
            Assert.Equal(texto.ToString(), File.ReadAllText(informe));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
