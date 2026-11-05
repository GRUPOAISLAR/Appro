using Bravo.Video.Contratos;
using Bravo.Video.Estadias;

namespace Bravo.Video.Tests;

public class EstadiasPruebas
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 8, 0, 0, TimeSpan.FromHours(-3));
    private static readonly ReglaEstadias Regla = new() { DuracionMaxima = TimeSpan.FromHours(12) };
    private static int _contador;

    private static LecturaPatente Patente(string? normalizada) => new()
    {
        Leida = normalizada ?? "???",
        Normalizada = normalizada,
        Pais = "AR",
        Formato = normalizada is null ? null : "AR_MERCOSUR_AUTO",
        Valida = normalizada is not null,
    };

    private static VehiculoIngreso In(string? patente, double minutos, string sede = "S1") => new()
    {
        EventId = Id(), Sede = sede, Dispositivo = "D", Camara = "c", Ts = T0.AddMinutes(minutos),
        Fuente = new Fuente("frigate", "0.17.1"), Patente = Patente(patente),
    };

    private static VehiculoEgreso Out(string? patente, double minutos, string sede = "S1") => new()
    {
        EventId = Id(), Sede = sede, Dispositivo = "D", Camara = "c", Ts = T0.AddMinutes(minutos),
        Fuente = new Fuente("frigate", "0.17.1"), Patente = Patente(patente),
    };

    private static Guid Id() => new($"00000000-0000-0000-0000-{Interlocked.Increment(ref _contador):D12}");

    [Fact]
    public void Empareja_ingreso_y_egreso_y_guarda_los_dos_ids()
    {
        var i = In("AA000AA", 0);
        var o = Out("AA000AA", 45);

        var r = EmparejadorEstadias.Emparejar([o, i], Regla, T0.AddHours(1));

        var e = Assert.Single(r.Estadias);
        Assert.Equal(i.EventId, e.EventoIngreso);
        Assert.Equal(o.EventId, e.EventoEgreso);
        Assert.Equal(TimeSpan.FromMinutes(45), e.Duracion);
        Assert.Empty(r.IngresosSinPareja);
        Assert.Empty(r.EgresosSinPareja);
    }

    [Fact]
    public void Doble_ingreso_deja_el_primero_sin_pareja()
    {
        var i1 = In("AA000AA", 0);
        var i2 = In("AA000AA", 30);
        var o = Out("AA000AA", 60);

        var r = EmparejadorEstadias.Emparejar([i1, i2, o], Regla, T0.AddHours(2));

        Assert.Equal(i2.EventId, Assert.Single(r.Estadias).EventoIngreso);
        var suelto = Assert.Single(r.IngresosSinPareja);
        Assert.Equal((i1.EventId, MotivoIngresoSinPareja.ReemplazadoPorOtroIngreso), (suelto.EventId, suelto.Motivo));
    }

    [Fact]
    public void Egreso_sin_ingreso()
    {
        var o = Out("AA000AA", 10);
        var r = EmparejadorEstadias.Emparejar([o], Regla, T0.AddHours(1));

        Assert.Equal(MotivoEgresoSinPareja.SinIngresoPrevio, Assert.Single(r.EgresosSinPareja).Motivo);
    }

    [Fact]
    public void Supera_la_duracion_maxima()
    {
        var i = In("AA000AA", 0);
        var o = Out("AA000AA", 13 * 60);
        var r = EmparejadorEstadias.Emparejar([i, o], Regla, T0.AddDays(1));

        Assert.Empty(r.Estadias);
        Assert.Equal(MotivoIngresoSinPareja.SuperoDuracionMaxima, Assert.Single(r.IngresosSinPareja).Motivo);
        Assert.Equal(MotivoEgresoSinPareja.IngresoDemasiadoAntiguo, Assert.Single(r.EgresosSinPareja).Motivo);
    }

    [Fact]
    public void Exactamente_la_duracion_maxima_se_acepta()
    {
        var r = EmparejadorEstadias.Emparejar([In("AA000AA", 0), Out("AA000AA", 12 * 60)], Regla, T0.AddDays(1));
        Assert.Single(r.Estadias);
    }

    [Fact]
    public void Ingreso_sin_egreso_abierto_o_vencido_segun_el_corte()
    {
        var i = In("AA000AA", 0);

        Assert.Equal(MotivoIngresoSinPareja.Abierto,
            Assert.Single(EmparejadorEstadias.Emparejar([i], Regla, T0.AddHours(3)).IngresosSinPareja).Motivo);
        Assert.Equal(MotivoIngresoSinPareja.SuperoDuracionMaxima,
            Assert.Single(EmparejadorEstadias.Emparejar([i], Regla, T0.AddHours(13)).IngresosSinPareja).Motivo);
    }

    [Fact]
    public void Separa_por_sede_y_aparta_patentes_invalidas()
    {
        var invalida = In(null, 5);
        var r = EmparejadorEstadias.Emparejar(
            [In("AA000AA", 0, "S1"), Out("AA000AA", 30, "S2"), invalida], Regla, T0.AddHours(1));

        Assert.Empty(r.Estadias);
        Assert.Single(r.IngresosSinPareja);
        Assert.Single(r.EgresosSinPareja);
        Assert.Equal(invalida.EventId, Assert.Single(r.SinPatenteValida));
    }

    [Fact]
    public void A_igual_hora_el_ingreso_va_primero_y_los_duplicados_cuentan_una_vez()
    {
        var i = In("AA000AA", 0);
        var o = Out("AA000AA", 0);

        var r = EmparejadorEstadias.Emparejar([o, i, i, o], Regla, T0.AddHours(1));

        Assert.Equal(TimeSpan.Zero, Assert.Single(r.Estadias).Duracion);
        Assert.Empty(r.EgresosSinPareja);
    }

    [Fact]
    public void Compara_horas_en_utc_aunque_vengan_con_distinta_zona()
    {
        var i = In("AA000AA", 0);
        var o = Out("AA000AA", 30) with { Ts = T0.AddMinutes(30).ToOffset(TimeSpan.Zero) };

        var r = EmparejadorEstadias.Emparejar([i, o], Regla, T0.AddHours(1));
        Assert.Equal(TimeSpan.FromMinutes(30), Assert.Single(r.Estadias).Duracion);
    }

    [Fact]
    public void Resumen_promedio_y_mediana()
    {
        var r = EmparejadorEstadias.Emparejar(
            [
                In("AA000AA", 0), Out("AA000AA", 10),
                In("AA000AB", 0), Out("AA000AB", 20),
                In("AA000AC", 0), Out("AA000AC", 60),
                In("AA000AD", 0), Out("AA000AD", 90),
            ],
            Regla, T0.AddHours(2));

        var resumen = ResumenEstadias.Calcular(r.Estadias);

        Assert.Equal(4, resumen.Cantidad);
        Assert.Equal(2700m, resumen.PromedioSegundos); // (600 + 1200 + 3600 + 5400) / 4
        Assert.Equal(2400m, resumen.MedianaSegundos); // (1200 + 3600) / 2
        Assert.Equal(new ResumenEstadias(0, null, null), ResumenEstadias.Calcular([]));
    }

    [Fact]
    public void Duracion_maxima_invalida()
    {
        Assert.Throws<ArgumentException>(() =>
            EmparejadorEstadias.Emparejar([], new ReglaEstadias { DuracionMaxima = TimeSpan.Zero }, T0));
    }
}
