using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bravo.Video.Contratos;
using Bravo.Video.Ingesta;
using Bravo.Video.Ingesta.Api;
using Bravo.Video.Ingesta.Datos;
using Bravo.Video.Ingesta.Equipos;
using Bravo.Video.Ingesta.Tareas;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bravo.Video.Ingesta.Tests;

/// <summary>
/// Levanta la ingesta real sobre SQLite en un archivo temporal. La configuración
/// va por variables de entorno, como en el servidor, así que todas las pruebas
/// que la usan comparten una sola instancia (colección sin paralelismo).
/// </summary>
public sealed class ServidorDePrueba : IAsyncLifetime
{
    public const string Sede = "AR-PRUEBA-01";
    public const string Dispositivo = "minipc-01";

    private readonly string _archivo = Path.Combine(Path.GetTempPath(), $"video-ingesta-{Guid.NewGuid():N}.db");

    public WebApplicationFactory<Program> Fabrica { get; private set; } = null!;
    public string Token { get; } = Tokens.Generar();

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable(ConfiguracionIngesta.VarProveedor, "Sqlite");
        Environment.SetEnvironmentVariable(ConfiguracionIngesta.VarConexion, $"Data Source={_archivo}");
        Environment.SetEnvironmentVariable(ConfiguracionIngesta.VarClaveHash, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable(ConfiguracionIngesta.VarRetencionDias, "30");

        Fabrica = new WebApplicationFactory<Program>();
        await using var alcance = Fabrica.Services.CreateAsyncScope();
        var db = alcance.ServiceProvider.GetRequiredService<VideoDbContext>();
        await Base.PrepararAsync(db, ProveedorBase.Sqlite);
        db.Equipos.Add(new Equipo { Dispositivo = Dispositivo, Sede = Sede, TokenHash = Tokens.Hash(Token), CreadoUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public HttpClient Cliente(string? token = null)
    {
        var cliente = Fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token ?? Token);
        return cliente;
    }

    public async Task<T> ConBaseAsync<T>(Func<VideoDbContext, Task<T>> accion)
    {
        await using var alcance = Fabrica.Services.CreateAsyncScope();
        return await accion(alcance.ServiceProvider.GetRequiredService<VideoDbContext>());
    }

    public async Task DisposeAsync()
    {
        await Fabrica.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_archivo);
    }
}

[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ColeccionIngesta : ICollectionFixture<ServidorDePrueba>
{
    public const string Nombre = "ingesta";
}

[Collection(ColeccionIngesta.Nombre)]
public class IngestaPruebas(ServidorDePrueba servidor)
{
    private static VehiculoIngreso Ingreso(DateTimeOffset? ts = null, string patente = "AA000AA", string sede = ServidorDePrueba.Sede) => new()
    {
        EventId = Guid.NewGuid(),
        Sede = sede,
        Dispositivo = ServidorDePrueba.Dispositivo,
        Camara = "porton",
        Ts = ts ?? DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)),
        Fuente = new Fuente("frigate", "0.17.1"),
        Patente = new LecturaPatente
        {
            Leida = patente, Normalizada = patente, Pais = "AR", Formato = "AR_MERCOSUR_AUTO", Valida = true, Confianza = 0.9m,
        },
        IdOrigen = "obj-" + Guid.NewGuid().ToString("N")[..6],
    };

    private static StringContent Lote(params object[] eventos)
    {
        var arreglo = new JsonArray();
        foreach (var e in eventos)
        {
            arreglo.Add(e is EventoVideo ev
                ? JsonSerializer.SerializeToNode(ev, OpcionesJson.Eventos)
                : JsonNode.Parse((string)e));
        }

        return new StringContent(new JsonObject { ["eventos"] = arreglo }.ToJsonString(), Encoding.UTF8, "application/json");
    }

    private static async Task<RespuestaLote> Leer(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return (await r.Content.ReadFromJsonAsync<RespuestaLote>(opciones))!;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bvv_inventado")]
    [InlineData("otro-esquema")]
    public async Task Sin_token_valido_responde_401(string? token)
    {
        var cliente = servidor.Fabrica.CreateClient();
        if (token is not null)
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var r = await cliente.PostAsync("/api/v1/eventos", Lote(Ingreso()));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Guarda_y_un_reenvio_no_duplica()
    {
        var a = Ingreso();
        var b = Ingreso(patente: "AA000AB");
        var antes = DateTimeOffset.UtcNow;

        var primero = await Leer(await servidor.Cliente().PostAsync("/api/v1/eventos", Lote(a, b)));
        var segundo = await Leer(await servidor.Cliente().PostAsync("/api/v1/eventos", Lote(a, b)));

        Assert.Equal((2, 0), (primero.Aceptados, primero.Duplicados));
        Assert.Equal((0, 2), (segundo.Aceptados, segundo.Duplicados));
        Assert.Empty(primero.Rechazados);
        Assert.InRange(primero.HoraServidor, antes.AddSeconds(-5), DateTimeOffset.UtcNow.AddSeconds(5));

        var guardado = await servidor.ConBaseAsync(db => db.Eventos.SingleAsync(e => e.EventId == a.EventId));
        Assert.Equal(TiposEvento.VehiculoIngreso, guardado.Tipo);
        Assert.Equal("AA000AA", guardado.PatenteNormalizada);
        Assert.StartsWith("h1:", guardado.PatenteHash);
        Assert.Equal(-180, guardado.TsOffsetMinutos);
        Assert.Equal(a.Ts.UtcDateTime, guardado.TsUtc, TimeSpan.FromMilliseconds(1));
        Assert.Equal(0.9m, guardado.PatenteConfianza);

        var equipo = await servidor.ConBaseAsync(db => db.Equipos.SingleAsync(e => e.Dispositivo == ServidorDePrueba.Dispositivo));
        Assert.NotNull(equipo.UltimoContactoUtc);
    }

    [Fact]
    public async Task Lote_mixto_acepta_lo_bueno_y_rechaza_con_motivo()
    {
        var bueno = Ingreso();
        var otraSede = Ingreso(sede: "OTRA-SEDE");
        var sinZona = JsonSerializer.SerializeToNode<EventoVideo>(Ingreso(), OpcionesJson.Eventos)!;
        sinZona["ts"] = "2026-11-04T10:00:00";

        var r = await Leer(await servidor.Cliente().PostAsync("/api/v1/eventos",
            Lote(bueno, sinZona.ToJsonString(), otraSede, bueno, "\"no es un objeto\"")));

        Assert.Equal(1, r.Aceptados);
        Assert.Equal(1, r.Duplicados); // el mismo eventId dos veces en el lote
        Assert.Equal([1, 2, 4], r.Rechazados.Select(x => x.Indice));
        Assert.Contains("zona horaria", r.Rechazados[0].Errores.Single());
        Assert.Contains(r.Rechazados[1].Errores, e => e.Contains("sede"));
        Assert.Equal(otraSede.EventId, r.Rechazados[1].EventId);
    }

    [Fact]
    public async Task Rechaza_lote_grande_y_cuerpo_invalido()
    {
        var grande = Lote(Enumerable.Range(0, ConfiguracionIngesta.MaximoEventosPorLote + 1).Select(_ => (object)Ingreso()).ToArray());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await servidor.Cliente().PostAsync("/api/v1/eventos", grande)).StatusCode);

        var basura = new StringContent("{no json", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await servidor.Cliente().PostAsync("/api/v1/eventos", basura)).StatusCode);

        var sinEventos = new StringContent("""{"otra":[]}""", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await servidor.Cliente().PostAsync("/api/v1/eventos", sinEventos)).StatusCode);
    }

    [Fact]
    public async Task Guarda_personas_y_latido()
    {
        var ahora = DateTimeOffset.UtcNow;
        var cruce = new PersonasCruce
        {
            EventId = Guid.NewGuid(), Sede = ServidorDePrueba.Sede, Dispositivo = ServidorDePrueba.Dispositivo,
            Camara = "sensor-hall", Ts = ahora, Fuente = new Fuente("milesight", "1.0"), Linea = "Puerta", Entradas = 3, Salidas = 1,
        };
        var latido = new DispositivoLatido
        {
            EventId = Guid.NewGuid(), Sede = ServidorDePrueba.Sede, Dispositivo = ServidorDePrueba.Dispositivo, Ts = ahora,
            Fuente = new Fuente("conector", "0.1.0"), VersionConector = "0.1.0",
            Motores = [new EstadoMotor("frigate", EstadosMotor.EnLinea)], ColaPendiente = 4, DesvioRelojMs = -120,
        };

        var r = await Leer(await servidor.Cliente().PostAsync("/api/v1/eventos", Lote(cruce, latido)));
        Assert.Equal(2, r.Aceptados);

        var l = await servidor.ConBaseAsync(db => db.Eventos.SingleAsync(e => e.EventId == latido.EventId));
        Assert.Equal((4, -120L), (l.ColaPendiente!.Value, l.DesvioRelojMs!.Value));
        Assert.Contains("en_linea", l.MotoresJson);
    }

    [Fact]
    public async Task Anonimiza_solo_lo_vencido_y_conserva_el_hash()
    {
        var viejo = Ingreso(ts: DateTimeOffset.UtcNow.AddDays(-40), patente: "AA111AA");
        var nuevo = Ingreso(ts: DateTimeOffset.UtcNow.AddDays(-1), patente: "AA222AA");
        await Leer(await servidor.Cliente().PostAsync("/api/v1/eventos", Lote(viejo, nuevo)));

        var ahora = DateTime.UtcNow;
        var primera = await servidor.ConBaseAsync(db => TareaAnonimizacion.AnonimizarAsync(db, ahora, TimeSpan.FromDays(30), default));
        var segunda = await servidor.ConBaseAsync(db => TareaAnonimizacion.AnonimizarAsync(db, ahora, TimeSpan.FromDays(30), default));

        Assert.True(primera >= 1);
        Assert.Equal(0, segunda);

        var v = await servidor.ConBaseAsync(db => db.Eventos.AsNoTracking().SingleAsync(e => e.EventId == viejo.EventId));
        var n = await servidor.ConBaseAsync(db => db.Eventos.AsNoTracking().SingleAsync(e => e.EventId == nuevo.EventId));

        Assert.Null(v.PatenteLeida);
        Assert.Null(v.PatenteNormalizada);
        Assert.NotNull(v.PatenteHash);
        Assert.NotNull(v.PatenteAnonimizadaUtc);
        Assert.Equal("AA222AA", n.PatenteNormalizada);
        Assert.Null(n.PatenteAnonimizadaUtc);
    }

    [Fact]
    public async Task Equipo_dado_de_baja_no_entra()
    {
        var token = Tokens.Generar();
        await servidor.ConBaseAsync(async db =>
        {
            db.Equipos.Add(new Equipo { Dispositivo = "minipc-baja", Sede = ServidorDePrueba.Sede, TokenHash = Tokens.Hash(token), Activo = false });
            return await db.SaveChangesAsync();
        });

        var r = await servidor.Cliente(token).PostAsync("/api/v1/eventos", Lote(Ingreso()));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}

public class ConfiguracionPruebas
{
    private static IConfiguration Config(Dictionary<string, string?> valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

    [Fact]
    public void Falta_todo_y_dice_que_sin_mostrar_valores()
    {
        var e = Assert.Throws<InvalidOperationException>(() => ConfiguracionIngesta.Leer(Config(new()
        {
            [ConfiguracionIngesta.VarClaveHash] = "valor-que-no-debe-aparecer",
        })));

        Assert.Contains(ConfiguracionIngesta.VarProveedor, e.Message);
        Assert.Contains(ConfiguracionIngesta.VarConexion, e.Message);
        Assert.Contains(ConfiguracionIngesta.VarClaveHash, e.Message);
        Assert.Contains(ConfiguracionIngesta.VarRetencionDias, e.Message);
        Assert.DoesNotContain("valor-que-no-debe-aparecer", e.Message);
    }

    [Fact]
    public void Configuracion_completa()
    {
        var c = ConfiguracionIngesta.Leer(Config(new()
        {
            [ConfiguracionIngesta.VarProveedor] = "sqlserver",
            [ConfiguracionIngesta.VarConexion] = "Server=inventado",
            [ConfiguracionIngesta.VarClaveHash] = Convert.ToBase64String(new byte[32]),
            [ConfiguracionIngesta.VarRetencionDias] = "30",
        }));

        Assert.Equal(ProveedorBase.SqlServer, c.Proveedor);
        Assert.Equal(TimeSpan.FromDays(30), c.RetencionPatente);
        Assert.Equal(TimeSpan.FromMinutes(60), c.IntervaloAnonimizacion);
    }
}

public class ComandosPruebas
{
    [Fact]
    public async Task Alta_muestra_el_token_una_vez_y_reemplazar_lo_rota()
    {
        var archivo = Path.Combine(Path.GetTempPath(), $"video-comandos-{Guid.NewGuid():N}.db");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ConfiguracionIngesta.VarProveedor] = "Sqlite",
            [ConfiguracionIngesta.VarConexion] = $"Data Source={archivo}",
            [ConfiguracionIngesta.VarClaveHash] = Convert.ToBase64String(new byte[32]),
            [ConfiguracionIngesta.VarRetencionDias] = "30",
        }).Build();

        try
        {
            Assert.Equal(0, await Comandos.EjecutarAsync(["migrar"], TextWriter.Null, config));

            var salida = new StringWriter();
            Assert.Equal(0, await Comandos.EjecutarAsync(["equipo-alta", "--dispositivo", "minipc-09", "--sede", "S"], salida, config));
            var token = salida.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1].Trim();
            Assert.StartsWith(Tokens.Prefijo, token);

            Assert.Equal(1, await Comandos.EjecutarAsync(["equipo-alta", "--dispositivo", "minipc-09", "--sede", "S"], TextWriter.Null, config));

            var otra = new StringWriter();
            Assert.Equal(0, await Comandos.EjecutarAsync(["equipo-alta", "--dispositivo", "minipc-09", "--sede", "S", "--reemplazar"], otra, config));
            var nuevo = otra.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1].Trim();
            Assert.NotEqual(token, nuevo);

            Assert.Equal(0, await Comandos.EjecutarAsync(["equipo-baja", "--dispositivo", "minipc-09"], TextWriter.Null, config));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(archivo);
        }
    }
}
