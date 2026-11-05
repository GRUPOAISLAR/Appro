using System.Net;
using System.Text;
using System.Text.Json;
using Bravo.Video.Conector.Envio;
using Bravo.Video.Contratos;

namespace Bravo.Video.Conector.Tests;

public class ColaPruebas
{
    [Fact]
    public void Conserva_orden_y_sobrevive_a_un_reinicio()
    {
        var dir = Ayudas.DirectorioTemporal();
        var cola = Ayudas.Cola(dir);
        for (var i = 0; i < 5; i++)
        {
            Assert.True(cola.Encolar(Ayudas.Evento(i)));
        }

        var reiniciada = Ayudas.Cola(dir);
        Assert.Equal(5, reiniciada.Pendientes);

        var lote = reiniciada.Tomar(3);
        Assert.Equal(["box-0", "box-1", "box-2"], lote.Select(e => ((BoxLibre)e.Evento).Box));

        reiniciada.Confirmar(lote.Select(e => e.Archivo));
        Assert.Equal(2, reiniciada.Pendientes);
        Assert.Equal(["box-3", "box-4"], reiniciada.Tomar(10).Select(e => ((BoxLibre)e.Evento).Box));
    }

    [Fact]
    public void Llena_rechaza_lo_nuevo_sin_pisar_lo_viejo()
    {
        var cola = Ayudas.Cola(Ayudas.DirectorioTemporal(), maximo: 2);
        Assert.True(cola.Encolar(Ayudas.Evento(0)));
        Assert.True(cola.Encolar(Ayudas.Evento(1)));
        Assert.False(cola.Encolar(Ayudas.Evento(2)));

        Assert.Equal(1, cola.RechazadosPorLlena);
        Assert.Equal(["box-0", "box-1"], cola.Tomar(10).Select(e => ((BoxLibre)e.Evento).Box));
    }

    [Fact]
    public void Archivo_ilegible_va_a_cuarentena_y_temporales_se_borran()
    {
        var dir = Ayudas.DirectorioTemporal();
        var cola = Ayudas.Cola(dir);
        cola.Encolar(Ayudas.Evento(0));
        File.WriteAllText(Path.Combine(dir, "0000000000000000000-000000000-roto.json"), "{no es json");
        File.WriteAllText(Path.Combine(dir, "a-medias.tmp"), "{");

        var reiniciada = Ayudas.Cola(dir);
        var lote = reiniciada.Tomar(10);

        Assert.Single(lote);
        Assert.Equal(1, reiniciada.EnCuarentena);
        Assert.Equal(1, reiniciada.Pendientes);
        Assert.False(File.Exists(Path.Combine(dir, "a-medias.tmp")));
    }
}

public class EnvioPruebas
{
    private sealed class Ingesta(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> Cuerpos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var cuerpo = await request.Content!.ReadAsStringAsync(ct);
            Cuerpos.Add(cuerpo);
            return responder(request, cuerpo);
        }
    }

    private static (EnviadorCola Enviador, Cola.ColaDisco Cola, EstadoConector Estado, Ingesta Ingesta) Armar(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder)
    {
        var dir = Ayudas.DirectorioTemporal();
        var cola = Ayudas.Cola(dir);
        var estado = new EstadoConector(TimeProvider.System);
        var ingesta = new Ingesta(responder);
        var enviador = new EnviadorCola(cola, new HttpClient(ingesta), Ayudas.Configuracion(dir), estado, TimeProvider.System, Ayudas.Log<EnviadorCola>());
        return (enviador, cola, estado, ingesta);
    }

    private static HttpResponseMessage Ok(DateTimeOffset horaServidor, int aceptados) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { horaServidor, aceptados, duplicados = 0, rechazados = Array.Empty<object>() },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task Exito_borra_de_la_cola_manda_token_y_tipo_y_mide_desvio()
    {
        var (enviador, cola, estado, ingesta) = Armar((pedido, _) =>
        {
            Assert.Equal("Bearer", pedido.Headers.Authorization!.Scheme);
            Assert.EndsWith("/api/v1/eventos", pedido.RequestUri!.AbsolutePath);

            // El servidor "atrasa" 10 s: el equipo adelanta unos 10 000 ms.
            return Ok(DateTimeOffset.UtcNow.AddSeconds(-10), 2);
        });
        cola.Encolar(Ayudas.Evento(0));
        cola.Encolar(Ayudas.Evento(1));

        Assert.Equal(ResultadoEnvio.Procesado, await enviador.EnviarUnLoteAsync(default));

        Assert.Equal(0, cola.Pendientes);
        Assert.Contains("\"tipo\":\"BOX_LIBRE\"", ingesta.Cuerpos.Single());
        Assert.InRange(estado.DesvioRelojMs!.Value, 9_000, 11_000);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Falla_transitoria_conserva_el_lote(HttpStatusCode codigo)
    {
        var (enviador, cola, _, _) = Armar((_, _) => new HttpResponseMessage(codigo));
        cola.Encolar(Ayudas.Evento(0));

        Assert.Equal(ResultadoEnvio.Reintentar, await enviador.EnviarUnLoteAsync(default));
        Assert.Equal(1, cola.Pendientes);
    }

    [Fact]
    public async Task Sin_red_conserva_el_lote()
    {
        var (enviador, cola, _, _) = Armar((_, _) => throw new HttpRequestException("sin red"));
        cola.Encolar(Ayudas.Evento(0));

        Assert.Equal(ResultadoEnvio.Reintentar, await enviador.EnviarUnLoteAsync(default));
        Assert.Equal(1, cola.Pendientes);
    }

    [Fact]
    public async Task Lote_rechazado_entero_va_a_cuarentena()
    {
        var (enviador, cola, _, _) = Armar((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest));
        cola.Encolar(Ayudas.Evento(0));

        Assert.Equal(ResultadoEnvio.Cuarentena, await enviador.EnviarUnLoteAsync(default));
        Assert.Equal(0, cola.Pendientes);
        Assert.Equal(1, cola.EnCuarentena);
    }

    [Fact]
    public async Task Manda_de_a_500()
    {
        var (enviador, cola, _, ingesta) = Armar((_, _) => Ok(DateTimeOffset.UtcNow, 500));
        for (var i = 0; i < 501; i++)
        {
            cola.Encolar(Ayudas.Evento(i));
        }

        await enviador.EnviarUnLoteAsync(default);

        Assert.Equal(1, cola.Pendientes);
        using var doc = JsonDocument.Parse(ingesta.Cuerpos.Single());
        Assert.Equal(EnviadorCola.TamanioLote, doc.RootElement.GetProperty("eventos").GetArrayLength());
    }
}
