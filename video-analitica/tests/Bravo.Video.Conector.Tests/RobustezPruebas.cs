using System.Net;
using System.Text;
using Bravo.Video.Conector.Envio;
using Bravo.Video.Contratos;

namespace Bravo.Video.Conector.Tests;

/// <summary>Casos de la revisión independiente: nada de esto puede apagar el conector ni trabar la cola.</summary>
public class RobustezPruebas
{
    private sealed class Responder(Func<HttpResponseMessage> respuesta) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respuesta());
    }

    [Fact]
    public void Archivo_con_json_valido_sin_tipo_va_a_cuarentena_sin_excepcion()
    {
        var dir = Ayudas.DirectorioTemporal();
        var cola = Ayudas.Cola(dir);
        File.WriteAllText(Path.Combine(dir, "0000000000000000000-000000000-sintipo.json"), """{"eventId":"x"}""");
        cola.Encolar(Ayudas.Evento(1));

        var reiniciada = Ayudas.Cola(dir);
        var lote = reiniciada.Tomar(10);

        Assert.Equal("box-1", ((BoxLibre)Assert.Single(lote).Evento).Box);
        Assert.Equal(1, reiniciada.EnCuarentena);
    }

    [Fact]
    public async Task Un_200_que_no_es_de_la_ingesta_no_borra_la_cola()
    {
        var dir = Ayudas.DirectorioTemporal();
        var cola = Ayudas.Cola(dir);
        cola.Encolar(Ayudas.Evento(0));
        var portalCautivo = new Responder(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Iniciar sesión en el wifi</html>", Encoding.UTF8, "text/html"),
        });
        var enviador = new EnviadorCola(cola, new HttpClient(portalCautivo), Ayudas.Configuracion(dir),
            new EstadoConector(TimeProvider.System), TimeProvider.System, Ayudas.Log<EnviadorCola>());

        Assert.Equal(ResultadoEnvio.Reintentar, await enviador.EnviarUnLoteAsync(default));
        Assert.Equal(1, cola.Pendientes);
    }

    [Fact]
    public async Task Una_excepcion_inesperada_no_detiene_el_enviador()
    {
        var dir = Ayudas.DirectorioTemporal();
        var cola = Ayudas.Cola(dir);
        cola.Encolar(Ayudas.Evento(0));
        var llamadas = 0;
        var explota = new Responder(() =>
        {
            Interlocked.Increment(ref llamadas);
            throw new InvalidOperationException("algo no previsto");
        });
        var enviador = new EnviadorCola(cola, new HttpClient(explota), Ayudas.Configuracion(dir),
            new EstadoConector(TimeProvider.System), TimeProvider.System, Ayudas.Log<EnviadorCola>());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await enviador.StartAsync(cts.Token);
        while (Volatile.Read(ref llamadas) < 2 && !cts.IsCancellationRequested)
        {
            await Task.Delay(50);
        }

        // Si la excepción hubiera salido de ExecuteAsync, no habría un segundo intento.
        Assert.True(Volatile.Read(ref llamadas) >= 2);
        Assert.False(enviador.ExecuteTask!.IsFaulted);
        Assert.Equal(1, cola.Pendientes);
        await enviador.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Encolar_todos_es_todo_o_nada()
    {
        var cola = Ayudas.Cola(Ayudas.DirectorioTemporal(), maximo: 3);
        cola.Encolar(Ayudas.Evento(0));

        Assert.False(cola.EncolarTodos([Ayudas.Evento(1), Ayudas.Evento(2), Ayudas.Evento(3)]));
        Assert.Equal(1, cola.Pendientes);

        Assert.True(cola.EncolarTodos([Ayudas.Evento(1), Ayudas.Evento(2)]));
        Assert.Equal(3, cola.Pendientes);
    }
}
