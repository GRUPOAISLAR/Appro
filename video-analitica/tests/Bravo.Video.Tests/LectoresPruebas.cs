using Bravo.Video.Contratos;
using Bravo.Video.Lectores;
using Bravo.Video.Patentes;

namespace Bravo.Video.Tests;

// Mensajes armados a mano con el formato documentado de cada motor. Patentes,
// direcciones IP e identificadores son inventados.
public class LectoresPruebas
{
    private static readonly TimeZoneInfo BuenosAires = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");
    private static readonly DateTimeOffset Recibido = new(2026, 11, 4, 13, 0, 0, TimeSpan.Zero);

    private static ConfiguracionSede Sede(params ConfiguracionCamara[] camaras) => new()
    {
        Sede = "AR-PRUEBA-01",
        Dispositivo = "minipc-01",
        Pais = Pais.AR,
        ZonaHoraria = BuenosAires,
        Camaras = camaras.ToDictionary(c => c.Camara),
    };

    private static string EventoFrigate(string tipo, string id = "1762261200.5-abc123", string label = "car",
        string? patente = null, string? puntaje = null, string zonas = "[]") => $$"""
        {"type":"{{tipo}}",
         "before":{},
         "after":{"id":"{{id}}","camera":"porton","label":"{{label}}","sub_label":null,
                  "start_time":1762261200.5,"end_time":{{(tipo == "end" ? "1762261210.25" : "null")}},
                  "entered_zones":{{zonas}},"current_zones":[],
                  "recognized_license_plate":{{(patente is null ? "null" : $"\"{patente}\"")}},
                  "recognized_license_plate_score":{{puntaje ?? "null"}} } }
        """;

    [Fact]
    public void Frigate_lee_evento_con_patente()
    {
        var m = LectorFrigate.LeerEvento(EventoFrigate("update", patente: "AA000AA", puntaje: "0.91", zonas: """["entrada"]"""));

        Assert.Equal("update", m.Tipo);
        Assert.Equal("porton", m.Camara);
        Assert.Equal("AA000AA", m.Patente);
        Assert.Equal(0.91m, m.PuntajePatente);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1762261200500), m.Inicio);
        Assert.Equal(["entrada"], m.ZonasIngresadas);
    }

    [Fact]
    public void Frigate_lee_actualizacion_lpr_e_ignora_otras()
    {
        var lpr = LectorFrigate.LeerActualizacion("""
            {"type":"lpr","id":"1762261200.5-abc123","name":null,"plate":"AA000AB","score":0.97,
             "camera":"porton","timestamp":1762261203.1,"plate_box":[1,2,3,4]}
            """);
        Assert.NotNull(lpr);
        Assert.Equal("AA000AB", lpr.Patente);
        Assert.Equal(0.97m, lpr.Puntaje);

        Assert.Null(LectorFrigate.LeerActualizacion("""{"type":"description","id":"x","camera":"c","description":"..."}"""));
        Assert.Null(LectorFrigate.LeerActualizacion("""{"type":"lpr","plate":"AA000AB","camera":"porton"}"""));
    }

    [Fact]
    public void Frigate_disponibilidad()
    {
        Assert.True(LectorFrigate.LeerDisponibilidad("online"));
        Assert.False(LectorFrigate.LeerDisponibilidad("offline\n"));
        Assert.Null(LectorFrigate.LeerDisponibilidad("otra"));
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("[]")]
    [InlineData("""{"type":"new"}""")]
    [InlineData("""{"type":"new","after":{"id":"x","camera":"c","label":"car"}}""")]
    public void Frigate_mensaje_invalido(string json)
    {
        Assert.Throws<MensajeInvalidoException>(() => LectorFrigate.LeerEvento(json));
    }

    [Fact]
    public void Acumulador_se_queda_con_la_mejor_lectura_y_emite_al_final()
    {
        var acumulador = new AcumuladorFrigate("0.17.1");

        Assert.Null(acumulador.Procesar(LectorFrigate.LeerEvento(EventoFrigate("new")), Recibido));
        acumulador.Procesar(new ActualizacionPatenteFrigate
            { Id = "1762261200.5-abc123", Camara = "porton", Patente = "AA0OOAA", Puntaje = 0.80m }, Recibido);
        acumulador.Procesar(new ActualizacionPatenteFrigate
            { Id = "1762261200.5-abc123", Camara = "porton", Patente = "AA000AA", Puntaje = 0.95m }, Recibido);
        acumulador.Procesar(new ActualizacionPatenteFrigate
            { Id = "1762261200.5-abc123", Camara = "porton", Patente = "AA000AX", Puntaje = 0.95m }, Recibido);

        var lectura = acumulador.Procesar(
            LectorFrigate.LeerEvento(EventoFrigate("end", patente: "AA000AA", puntaje: "0.90", zonas: """["entrada"]""")),
            Recibido);

        Assert.NotNull(lectura);
        Assert.Equal("AA000AA", lectura.PatenteLeida); // 0.95 gana a 0.90; el empate a 0.95 se queda con la primera
        Assert.Equal(0.95m, lectura.Confianza);
        Assert.Equal(["entrada"], lectura.Zonas);
        Assert.Equal(0, acumulador.Abiertos);
    }

    [Fact]
    public void Acumulador_emite_vehiculo_sin_patente_e_ignora_personas()
    {
        var acumulador = new AcumuladorFrigate("0.17.1");

        var auto = acumulador.Procesar(LectorFrigate.LeerEvento(EventoFrigate("end")), Recibido);
        Assert.NotNull(auto);
        Assert.Null(auto.PatenteLeida);

        Assert.Null(acumulador.Procesar(LectorFrigate.LeerEvento(EventoFrigate("end", id: "p1", label: "person")), Recibido));
    }

    [Fact]
    public void Acumulador_purga_objetos_sin_fin()
    {
        var acumulador = new AcumuladorFrigate("0.17.1");
        acumulador.Procesar(LectorFrigate.LeerEvento(EventoFrigate("new")), Recibido);

        Assert.Equal(0, acumulador.Purgar(Recibido));
        Assert.Equal(1, acumulador.Purgar(Recibido.AddSeconds(1)));
        Assert.Equal(0, acumulador.Abiertos);
    }

    private const string AlertaHikvision = """
        <?xml version="1.0" encoding="UTF-8"?>
        <EventNotificationAlert version="2.0" xmlns="http://www.isapi.org/ver20/XMLSchema">
          <ipAddress>192.0.2.10</ipAddress>
          <channelID>1</channelID>
          <dateTime>2026-11-04T10:15:30-03:00</dateTime>
          <activePostCount>1</activePostCount>
          <eventType>ANPR</eventType>
          <eventState>active</eventState>
          <ANPR>
            <country>AR</country>
            <licensePlate>AA000AA</licensePlate>
            <line>1</line>
            <direction>forward</direction>
            <confidenceLevel>87</confidenceLevel>
          </ANPR>
          <UUID>00000000-0000-0000-0000-00000000abcd</UUID>
        </EventNotificationAlert>
        """;

    [Fact]
    public void Hikvision_lee_alerta_anpr()
    {
        var l = LectorHikvisionAnpr.Leer(AlertaHikvision, "anpr-ingreso", "V5.7.0", BuenosAires);

        Assert.NotNull(l);
        Assert.Equal("AA000AA", l.PatenteLeida);
        Assert.Equal(0.87m, l.Confianza);
        Assert.Equal("forward", l.Sentido);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 10, 15, 30, TimeSpan.FromHours(-3)), l.Ts);
        Assert.Equal("00000000-0000-0000-0000-00000000abcd", l.IdOrigen);
    }

    [Fact]
    public void Hikvision_sin_zona_usa_la_de_la_sede_y_unknown_es_sin_patente()
    {
        var xml = AlertaHikvision
            .Replace("2026-11-04T10:15:30-03:00", "2026-11-04T10:15:30")
            .Replace("<licensePlate>AA000AA</licensePlate>", "<licensePlate>unknown</licensePlate>")
            .Replace("<UUID>00000000-0000-0000-0000-00000000abcd</UUID>", "");

        var l = LectorHikvisionAnpr.Leer(xml, "anpr-ingreso", "V5.7.0", BuenosAires);

        Assert.NotNull(l);
        Assert.Null(l.PatenteLeida);
        Assert.Equal(TimeSpan.FromHours(-3), l.Ts.Offset);
        Assert.Equal($"{l.Ts.ToUnixTimeMilliseconds()}:-", l.IdOrigen);
    }

    [Fact]
    public void Hikvision_ignora_alertas_que_no_son_anpr_y_rechaza_dtd()
    {
        var latido = AlertaHikvision.Replace("<eventType>ANPR</eventType>", "<eventType>videoloss</eventType>");
        Assert.Null(LectorHikvisionAnpr.Leer(latido, "c", "v", BuenosAires));

        var conDtd = """<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e "x">]><EventNotificationAlert/>""";
        Assert.Throws<MensajeInvalidoException>(() => LectorHikvisionAnpr.Leer(conDtd, "c", "v", BuenosAires));
    }

    [Fact]
    public void Milesight_lee_lineas()
    {
        var conteos = LectorMilesight.Leer("""
            {"device_info":{"device_name":"sensor-hall"},"line_trigger_data":[
              {"line":1,"line_name":"Puerta","line_uuid":"u1","in":3,"out":1},
              {"line":2,"line_uuid":"u2","in":0,"out":2}]}
            """);

        Assert.Equal([new ConteoLinea("Puerta", 3, 1), new ConteoLinea("2", 0, 2)], conteos);
    }

    [Theory]
    [InlineData("""{"otra":1}""")]
    [InlineData("""{"line_trigger_data":[{"line":1,"in":-1,"out":0}]}""")]
    [InlineData("""{"line_trigger_data":[{"line":1,"in":1}]}""")]
    [InlineData("""{"line_trigger_data":[{"in":1,"out":1}]}""")]
    public void Milesight_rechaza_reportes_incompletos(string json)
    {
        Assert.Throws<MensajeInvalidoException>(() => LectorMilesight.Leer(json));
    }

    private static LecturaVehiculo Lectura(string camara, IReadOnlyList<string>? zonas = null, string? sentido = null) => new()
    {
        Motor = Motores.Frigate,
        VersionMotor = "0.17.1",
        Camara = camara,
        IdOrigen = "obj-1",
        Ts = new DateTimeOffset(2026, 11, 4, 13, 15, 30, TimeSpan.Zero),
        PatenteLeida = "aa 0O0 aa",
        Confianza = 0.9m,
        Zonas = zonas ?? [],
        Sentido = sentido,
    };

    [Fact]
    public void Armador_camara_de_ingreso_normaliza_y_pasa_a_hora_de_la_sede()
    {
        var sede = Sede(new ConfiguracionCamara { Camara = "porton", Rol = RolCamara.Ingreso });
        var r = ArmadorEventos.Vehiculo(Lectura("porton"), sede);

        var ingreso = Assert.IsType<VehiculoIngreso>(r.Evento);
        Assert.Equal("AA000AA", ingreso.Patente!.Normalizada);
        Assert.Equal("aa 0O0 aa", ingreso.Patente.Leida);
        Assert.Equal(1, ingreso.Patente.Correcciones);
        Assert.Equal(TimeSpan.FromHours(-3), ingreso.Ts.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 10, 15, 30, TimeSpan.FromHours(-3)), ingreso.Ts);
        Assert.Empty(ValidadorEvento.Validar(ingreso));
    }

    [Fact]
    public void Armador_por_zona()
    {
        var sede = Sede(new ConfiguracionCamara
            { Camara = "porton", Rol = RolCamara.PorZona, ZonaIngreso = "entrada", ZonaEgreso = "salida" });

        Assert.IsType<VehiculoIngreso>(ArmadorEventos.Vehiculo(Lectura("porton", ["ENTRADA"]), sede).Evento);
        Assert.IsType<VehiculoEgreso>(ArmadorEventos.Vehiculo(Lectura("porton", ["salida"]), sede).Evento);

        var ambas = ArmadorEventos.Vehiculo(Lectura("porton", ["entrada", "salida"]), sede);
        Assert.Null(ambas.Evento);
        Assert.NotNull(ambas.MotivoDescarte);
        Assert.Null(ArmadorEventos.Vehiculo(Lectura("porton"), sede).Evento);
    }

    [Fact]
    public void Armador_por_sentido_y_camara_desconocida()
    {
        var sede = Sede(new ConfiguracionCamara
            { Camara = "anpr", Rol = RolCamara.PorSentido, SentidoIngreso = "forward", SentidoEgreso = "reverse" });

        Assert.IsType<VehiculoIngreso>(ArmadorEventos.Vehiculo(Lectura("anpr", sentido: "Forward"), sede).Evento);
        Assert.IsType<VehiculoEgreso>(ArmadorEventos.Vehiculo(Lectura("anpr", sentido: "reverse"), sede).Evento);
        Assert.Null(ArmadorEventos.Vehiculo(Lectura("anpr", sentido: "unknown"), sede).Evento);
        Assert.Equal("Cámara sin configurar.", ArmadorEventos.Vehiculo(Lectura("otra"), sede).MotivoDescarte);
    }

    [Fact]
    public void Armador_mismo_hecho_mismo_id_y_distinto_por_camara()
    {
        var sede = Sede(
            new ConfiguracionCamara { Camara = "a", Rol = RolCamara.Ingreso },
            new ConfiguracionCamara { Camara = "b", Rol = RolCamara.Ingreso });

        var uno = ArmadorEventos.Vehiculo(Lectura("a"), sede).Evento!;
        var otraVez = ArmadorEventos.Vehiculo(Lectura("a"), sede).Evento!;
        var otraCamara = ArmadorEventos.Vehiculo(Lectura("b"), sede).Evento!;

        Assert.Equal(uno.EventId, otraVez.EventId);
        Assert.NotEqual(uno.EventId, otraCamara.EventId);
    }

    [Fact]
    public void Armador_personas()
    {
        var sede = Sede();
        var e = ArmadorEventos.Personas(new ConteoLinea("Puerta", 3, 1), "sensor-hall", "1.0", Recibido, sede);

        Assert.Equal("Puerta", e.Linea);
        Assert.Equal(3, e.Entradas);
        Assert.Equal(TimeSpan.FromHours(-3), e.Ts.Offset);
        Assert.Empty(ValidadorEvento.Validar(e));
    }
}
