using Bravo.Video.Contratos;
using Bravo.Video.Evaluacion;
using Bravo.Video.Lectores;
using Bravo.Video.Patentes;
using Bravo.Video.Simulacion;

namespace Bravo.Video.Herramientas.Tests;

public class SimulacionPruebas
{
    internal static readonly TimeZoneInfo BuenosAires = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    internal static ParametrosSimulacion Parametros(int autos = 100, bool ruido = true, Pais pais = Pais.AR) => new()
    {
        Semilla = 7,
        Inicio = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.FromHours(-3)),
        AutosPorDia = autos,
        Pais = pais,
        RuidoDia = ruido ? RuidoLectura.DiaPorDefecto : RuidoLectura.Ninguno,
        RuidoNoche = ruido ? RuidoLectura.NochePorDefecto : RuidoLectura.Ninguno,
    };

    internal static InstalacionSimulada Instalacion(string motor, Pais pais = Pais.AR)
    {
        var frigate = motor == Motores.Frigate;
        var camaras = frigate
            ? new Dictionary<string, ConfiguracionCamara>
                { ["porton"] = new() { Camara = "porton", Rol = RolCamara.PorZona, ZonaIngreso = "entrada", ZonaEgreso = "salida" } }
            : new Dictionary<string, ConfiguracionCamara>
            {
                ["anpr-ingreso"] = new() { Camara = "anpr-ingreso", Rol = RolCamara.Ingreso },
                ["anpr-egreso"] = new() { Camara = "anpr-egreso", Rol = RolCamara.Egreso },
            };

        return new InstalacionSimulada
        {
            Sede = new ConfiguracionSede
                { Sede = "AR-PRUEBA-01", Dispositivo = "minipc-01", Pais = pais, ZonaHoraria = BuenosAires, Camaras = camaras },
            Motor = motor,
            CamaraIngreso = frigate ? "porton" : "anpr-ingreso",
            CamaraEgreso = frigate ? "porton" : "anpr-egreso",
        };
    }

    [Fact]
    public void Misma_semilla_mismo_escenario()
    {
        Assert.Equal(GeneradorEscenario.Generar(Parametros()), GeneradorEscenario.Generar(Parametros()));
        Assert.NotEqual(GeneradorEscenario.Generar(Parametros()), GeneradorEscenario.Generar(Parametros() with { Semilla = 8 }));
    }

    [Theory]
    [InlineData(Pais.AR)]
    [InlineData(Pais.MX)]
    public void Patentes_inventadas_unicas_validas_y_con_prefijo_Z(Pais pais)
    {
        var pasadas = GeneradorEscenario.Generar(Parametros(autos: 300, pais: pais));
        var patentes = pasadas.Select(p => p.PatenteReal).Distinct().ToList();

        Assert.Equal(300, patentes.Count);
        Assert.All(patentes, p =>
        {
            Assert.StartsWith("Z", p);
            Assert.True(NormalizadorPatentes.Normalizar(p, pais).Valida);
        });
    }

    [Fact]
    public void Cada_auto_entra_antes_de_salir_dentro_de_la_estadia()
    {
        var p = Parametros();
        foreach (var auto in GeneradorEscenario.Generar(p).GroupBy(x => x.Numero))
        {
            var ingreso = auto.Single(x => x.EsIngreso);
            var egreso = auto.Single(x => !x.EsIngreso);
            Assert.InRange(egreso.Ts - ingreso.Ts, p.EstadiaMinima, p.EstadiaMaxima);
            Assert.Equal(ingreso.PatenteReal, egreso.PatenteReal);
        }
    }

    [Fact]
    public void Sin_ruido_todo_se_detecta_y_se_lee_bien()
    {
        Assert.All(GeneradorEscenario.Generar(Parametros(ruido: false)), p =>
        {
            Assert.True(p.Detectada);
            Assert.Equal(p.PatenteReal, p.PatenteLeida);
        });
    }

    [Fact]
    public void Periodo_y_ruido_invalido()
    {
        var p = Parametros();
        Assert.Equal(Periodo.Noche, GeneradorEscenario.PeriodoDe(p.Inicio.AddHours(6.9), p));
        Assert.Equal(Periodo.Dia, GeneradorEscenario.PeriodoDe(p.Inicio.AddHours(7), p));
        Assert.Equal(Periodo.Noche, GeneradorEscenario.PeriodoDe(p.Inicio.AddHours(20), p));
        Assert.Throws<ArgumentException>(() => GeneradorEscenario.Generar(p with { RuidoDia = new RuidoLectura(0.5, 0.5, 0.1, 0) }));
    }

    [Fact]
    public void Los_mensajes_se_leen_con_los_lectores_reales()
    {
        var pasada = GeneradorEscenario.Generar(Parametros(ruido: false)).First();

        var frigate = Mensajes.Frigate(pasada, "porton", "entrada", "salida");
        Assert.Equal(3, frigate.Count);
        var fin = LectorFrigate.LeerEvento(frigate[^1].Carga);
        Assert.Equal("end", fin.Tipo);
        Assert.Equal(pasada.Ts, fin.Inicio);
        Assert.Equal(pasada.PatenteReal, LectorFrigate.LeerActualizacion(frigate[1].Carga)!.Patente);

        var anpr = LectorHikvisionAnpr.Leer(Mensajes.Hikvision(pasada)!, "anpr-ingreso", "sim", BuenosAires)!;
        Assert.Equal(pasada.PatenteReal, anpr.PatenteLeida);
        // La alerta de la cámara tiene resolución de segundos, como la real.
        Assert.Equal(pasada.Ts.ToUnixTimeSeconds(), anpr.Ts.ToUnixTimeSeconds());

        Assert.Empty(Mensajes.Frigate(pasada with { Detectada = false }, "porton", "entrada", "salida"));
        Assert.Null(Mensajes.Hikvision(pasada with { Detectada = false }));
    }

    [Theory]
    [InlineData(Motores.Frigate)]
    [InlineData(Motores.HikvisionAnpr)]
    public void El_eventId_de_la_verdad_coincide_con_el_del_conector(string motor)
    {
        var pasadas = GeneradorEscenario.Generar(Parametros());
        var instalacion = Instalacion(motor);

        var eventos = SimulacionSeca.Ejecutar(pasadas, instalacion);
        var etiquetas = Verdad.Etiquetas(pasadas, instalacion);

        Assert.Equal(pasadas.Count(p => p.Detectada), eventos.Count);
        Assert.Equal(
            etiquetas.Where(e => e.EventId is not null).Select(e => e.EventId!.Value).Order(),
            eventos.Select(e => e.EventId).Order());

        // Ingresos e egresos clasificados como en la realidad.
        var porId = pasadas.Where(p => p.Detectada).ToDictionary(instalacion.EventIdDe);
        Assert.All(eventos, e => Assert.Equal(porId[e.EventId].EsIngreso, e is VehiculoIngreso));
    }
}
