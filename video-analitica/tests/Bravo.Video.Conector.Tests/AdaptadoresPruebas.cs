using System.Net;
using System.Net.Sockets;
using System.Text;
using Bravo.Video.Conector.Adaptadores;
using Bravo.Video.Contratos;
using Bravo.Video.Lectores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Server;

namespace Bravo.Video.Conector.Tests;

public class ProcesadorFrigatePruebas
{
    internal static string Evento(string tipo, string zonas = """["entrada"]""") => $$"""
        {"type":"{{tipo}}","before":{},"after":{"id":"1762261200.5-abc123","camera":"porton","label":"car",
         "start_time":1762261200.5,"end_time":null,"entered_zones":{{zonas}},
         "recognized_license_plate":"AA000AA","recognized_license_plate_score":0.93} }
        """;

    private static (ProcesadorFrigate, Cola.ColaDisco, EstadoConector) Armar()
    {
        var dir = Ayudas.DirectorioTemporal();
        var cola = Ayudas.Cola(dir);
        var estado = new EstadoConector(TimeProvider.System);
        var config = Ayudas.Configuracion(dir, new ConfiguracionMqtt { Host = "localhost", VersionFrigate = "0.17.1" });
        return (new ProcesadorFrigate(cola, config, estado, TimeProvider.System, Ayudas.Log<ProcesadorFrigate>()), cola, estado);
    }

    [Fact]
    public void Del_mensaje_de_fin_sale_un_ingreso_en_la_cola()
    {
        var (procesador, cola, _) = Armar();
        procesador.Procesar(LectorFrigate.TopicoEventos, Encoding.UTF8.GetBytes(Evento("new")));
        Assert.Equal(0, cola.Pendientes);

        procesador.Procesar(LectorFrigate.TopicoEventos, Encoding.UTF8.GetBytes(Evento("end")));

        var evento = Assert.IsType<VehiculoIngreso>(Assert.Single(cola.Tomar(10)).Evento);
        Assert.Equal("AA000AA", evento.Patente!.Normalizada);
        Assert.Equal("frigate", evento.Fuente.Motor);
        Assert.Equal("0.17.1", evento.Fuente.Version);
    }

    [Fact]
    public void Cuenta_invalidos_y_descartes_y_lee_disponibilidad()
    {
        var (procesador, cola, estado) = Armar();

        procesador.Procesar(LectorFrigate.TopicoEventos, "basura"u8);
        procesador.Procesar(LectorFrigate.TopicoEventos, Encoding.UTF8.GetBytes(Evento("end", zonas: "[]")));
        procesador.Procesar(LectorFrigate.TopicoDisponible, "offline"u8);

        Assert.Equal(1, estado.Invalidos);
        Assert.Equal(1, estado.Descartes);
        Assert.False(estado.FrigateDisponible);
        Assert.Equal(0, cola.Pendientes);
    }
}

public class ClienteMqttPruebas
{
    [Fact]
    public async Task Con_broker_real_suscribe_y_encola()
    {
        var puerto = PuertoLibre();
        var servidor = new MqttServerFactory().CreateMqttServer(
            new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(puerto)
                .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
                .WithDefaultEndpointBoundIPV6Address(IPAddress.None) // el entorno de CI puede no tener IPv6
                .Build());
        await servidor.StartAsync();

        try
        {
            var dir = Ayudas.DirectorioTemporal();
            var cola = Ayudas.Cola(dir);
            var estado = new EstadoConector(TimeProvider.System);
            var config = Ayudas.Configuracion(dir, new ConfiguracionMqtt { Host = "127.0.0.1", Puerto = puerto, VersionFrigate = "0.17.1" });
            var procesador = new ProcesadorFrigate(cola, config, estado, TimeProvider.System, Ayudas.Log<ProcesadorFrigate>());
            var cliente = new ClienteMqttFrigate(procesador, config, estado, TimeProvider.System, Ayudas.Log<ClienteMqttFrigate>());

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await cliente.StartAsync(cts.Token);
            await Esperar(() => estado.MqttConectado, cts.Token);

            foreach (var (topico, carga) in new[]
                     {
                         (LectorFrigate.TopicoDisponible, "online"),
                         (LectorFrigate.TopicoEventos, ProcesadorFrigatePruebas.Evento("new")),
                         (LectorFrigate.TopicoEventos, ProcesadorFrigatePruebas.Evento("end")),
                     })
            {
                await servidor.InjectApplicationMessage(new InjectedMqttApplicationMessage(
                    new MqttApplicationMessageBuilder().WithTopic(topico).WithPayload(carga).Build()));
            }

            await Esperar(() => cola.Pendientes == 1, cts.Token);
            Assert.True(estado.FrigateDisponible);
            Assert.IsType<VehiculoIngreso>(cola.Tomar(1).Single().Evento);

            await cliente.StopAsync(CancellationToken.None);
        }
        finally
        {
            await servidor.StopAsync();
        }
    }

    private static int PuertoLibre()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    private static async Task Esperar(Func<bool> condicion, CancellationToken ct)
    {
        while (!condicion())
        {
            await Task.Delay(50, ct);
        }
    }
}

/// <summary>Pone una IP remota fija a cada pedido: TestServer no la completa.</summary>
internal sealed class FiltroIp(string ip) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> siguiente) => app =>
    {
        app.Use((contexto, resto) =>
        {
            contexto.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            return resto(contexto);
        });
        siguiente(app);
    };
}

[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ColeccionConector
{
    public const string Nombre = "conector-http";
}

[Collection(ColeccionConector.Nombre)]
public class ReceptorHttpPruebas : IDisposable
{
    private readonly string _dir = Ayudas.DirectorioTemporal();
    private readonly string _archivo;

    public ReceptorHttpPruebas()
    {
        _archivo = Path.Combine(_dir, "conector.json");
        File.WriteAllText(_archivo, System.Text.Json.JsonSerializer.Serialize(
            Ayudas.Archivo(Path.Combine(_dir, "cola")), ConfiguracionConector.OpcionesArchivo));
        Environment.SetEnvironmentVariable(ConfiguracionConector.VarArchivo, _archivo);
        Environment.SetEnvironmentVariable(ConfiguracionConector.VarToken, "bvv_token-de-prueba");
    }

    private WebApplicationFactory<Program> Fabrica(string ip) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IStartupFilter>(new FiltroIp(ip))));

    private const string Xml = """
        <EventNotificationAlert version="2.0" xmlns="http://www.isapi.org/ver20/XMLSchema">
          <dateTime>2026-11-04T10:15:30-03:00</dateTime><eventType>ANPR</eventType>
          <ANPR><licensePlate>AA000AA</licensePlate><direction>forward</direction><confidenceLevel>90</confidenceLevel></ANPR>
          <UUID>00000000-0000-0000-0000-0000000000a1</UUID>
        </EventNotificationAlert>
        """;

    [Fact]
    public async Task Anpr_multipart_encola_y_descarta_la_imagen()
    {
        await using var fabrica = Fabrica(Ayudas.IpCamara);
        var cliente = fabrica.CreateClient();

        using var contenido = new MultipartFormDataContent("limite-de-prueba");
        contenido.Add(new StringContent(Xml, Encoding.UTF8, "application/xml"), "anpr.xml", "anpr.xml");
        contenido.Add(new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0]) { Headers = { ContentType = new("image/jpeg") } }, "licensePlatePicture.jpg", "licensePlatePicture.jpg");

        var r = await cliente.PostAsync("/anpr/anpr-ingreso", contenido);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var cola = fabrica.Services.GetRequiredService<Cola.ColaDisco>();
        // El latido del arranque también está en la cola.
        var evento = Assert.Single(cola.Tomar(10).Select(e => e.Evento).OfType<VehiculoIngreso>());
        Assert.Equal("AA000AA", evento.Patente!.Normalizada);
        Assert.Equal(0.9m, evento.Patente.Confianza);

        // Ninguna imagen en disco: solo el evento en la cola.
        Assert.DoesNotContain(Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories), f => f.EndsWith(".jpg"));
    }

    [Fact]
    public async Task Anpr_xml_directo_e_ip_no_autorizada()
    {
        await using (var desdeOtraIp = Fabrica("192.0.2.99"))
        {
            var r = await desdeOtraIp.CreateClient().PostAsync("/anpr/anpr-ingreso", new StringContent(Xml, Encoding.UTF8, "application/xml"));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }

        await using var fabrica = Fabrica(Ayudas.IpCamara);
        var cliente = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsync("/anpr/anpr-ingreso", new StringContent(Xml, Encoding.UTF8, "text/xml"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.PostAsync("/anpr/otra", new StringContent(Xml))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsync("/anpr/anpr-ingreso", new StringContent("no xml"))).StatusCode);
    }

    [Fact]
    public async Task Conteo_encola_una_linea_por_evento()
    {
        await using var fabrica = Fabrica(Ayudas.IpSensor);
        var json = """{"line_trigger_data":[{"line":1,"line_name":"Puerta","in":3,"out":1},{"line":2,"in":0,"out":2}]}""";

        var r = await fabrica.CreateClient().PostAsync("/conteo/sensor-hall", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var eventos = fabrica.Services.GetRequiredService<Cola.ColaDisco>().Tomar(10)
            .Select(e => e.Evento).OfType<PersonasCruce>().ToList();
        Assert.Equal(["Puerta", "2"], eventos.Select(e => e.Linea));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}

public class ConfiguracionYLatidoPruebas
{
    [Fact]
    public void Junta_todos_los_errores()
    {
        var archivo = Ayudas.Archivo("") with
        {
            Pais = "UY",
            ZonaHoraria = "Marte/Olimpo",
            UrlIngesta = "http://servidor-remoto.ejemplo/",
            Camaras =
            [
                new CamaraArchivo { Camara = "z", Rol = RolCamara.PorZona },
                new CamaraArchivo { Camara = "z", Rol = RolCamara.Ingreso },
                new CamaraArchivo { Camara = "anpr", Motor = Motores.HikvisionAnpr, Rol = RolCamara.Ingreso },
            ],
        };

        var e = Assert.Throws<InvalidOperationException>(() =>
            ConfiguracionConector.Armar(archivo, Ayudas.Entorno(new() { [ConfiguracionConector.VarToken] = null })));

        foreach (var esperado in new[] { "directorioCola", "pais", "zonaHoraria", "urlIngesta", ConfiguracionConector.VarToken, "zonaIngreso", "repetida", "'ip'" })
        {
            Assert.Contains(esperado, e.Message);
        }
    }

    [Fact]
    public void Latido_informa_motores_y_repite_id_en_el_mismo_minuto()
    {
        var dir = Ayudas.DirectorioTemporal();
        var cola = Ayudas.Cola(dir);
        var estado = new EstadoConector(TimeProvider.System) { MqttConectado = true, DesvioRelojMs = 120 };
        estado.MensajeRecibido("hikvision-anpr:anpr-ingreso");
        var config = Ayudas.Configuracion(dir, new ConfiguracionMqtt { Host = "localhost", VersionFrigate = "0.17.1" });
        var latido = new Latido(cola, config, estado, TimeProvider.System, Ayudas.Log<Latido>());

        var a = latido.Armar();
        var b = latido.Armar();

        Assert.Empty(ValidadorEvento.Validar(a));
        Assert.Equal(120, a.DesvioRelojMs);
        Assert.Equal(
            ["mqtt=en_linea", "frigate=desconocido", "hikvision-anpr:anpr-ingreso=en_linea", "milesight:sensor-hall=desconocido"],
            a.Motores.Select(m => $"{m.Nombre}={m.Estado}"));

        // Si los dos caen en minutos distintos (borde del minuto), los ids difieren y está bien.
        if (a.Ts.ToUnixTimeSeconds() / 60 == b.Ts.ToUnixTimeSeconds() / 60)
        {
            Assert.Equal(a.EventId, b.EventId);
        }
    }
}
