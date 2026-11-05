using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Bravo.Video.Conector.Cola;
using Bravo.Video.Contratos;

namespace Bravo.Video.Conector.Envio;

/// <summary>Respuesta de la ingesta, con los mismos nombres que su <c>RespuestaLote</c>.</summary>
public sealed record RespuestaIngesta(DateTimeOffset HoraServidor, int Aceptados, int Duplicados, IReadOnlyList<RechazoIngesta> Rechazados);

public sealed record RechazoIngesta(int Indice, Guid? EventId, IReadOnlyList<string> Errores);

public enum ResultadoEnvio
{
    /// <summary>La ingesta procesó el lote; se borra de la cola.</summary>
    Procesado,

    /// <summary>Falla transitoria (red, 5xx, 401, 429): se reintenta más tarde con el mismo lote.</summary>
    Reintentar,

    /// <summary>La ingesta rechazó el lote entero (400/413): va a cuarentena para no frenar la cola.</summary>
    Cuarentena,
}

/// <summary>
/// Saca eventos de la cola de a lotes y los manda a la ingesta. Un lote solo se
/// borra cuando la ingesta lo confirmó; si no, se reintenta con espera creciente
/// (2 s, 4 s… hasta 5 min). Como los ids son determinísticos, reenviar lo que ya
/// había llegado no duplica nada.
/// </summary>
public sealed class EnviadorCola(
    ColaDisco cola,
    HttpClient http,
    ConfiguracionConector configuracion,
    EstadoConector estado,
    TimeProvider reloj,
    ILogger<EnviadorCola> log) : BackgroundService
{
    public const int TamanioLote = 500;
    private static readonly TimeSpan EsperaMinima = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions OpcionesRespuesta = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var espera = EsperaMinima;
        while (!ct.IsCancellationRequested)
        {
            if (cola.Pendientes == 0)
            {
                await cola.EsperarAsync(TimeSpan.FromSeconds(5), ct);
                continue;
            }

            ResultadoEnvio resultado;
            try
            {
                resultado = await EnviarUnLoteAsync(ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                // Nada que pase acá puede apagar el conector: Kestrel y MQTT corren en
                // el mismo proceso, y con ellos se perderían las lecturas nuevas.
                log.LogError("Error inesperado al enviar ({Tipo}); se reintenta.", e.GetType().Name);
                resultado = ResultadoEnvio.Reintentar;
            }

            if (resultado == ResultadoEnvio.Reintentar)
            {
                await Task.Delay(espera, reloj, ct);
                espera = TimeSpan.FromTicks(Math.Min(espera.Ticks * 2, EsperaMaxima.Ticks));
            }
            else
            {
                espera = EsperaMinima;
            }
        }
    }

    public async Task<ResultadoEnvio> EnviarUnLoteAsync(CancellationToken ct)
    {
        var lote = cola.Tomar(TamanioLote);
        if (lote.Count == 0)
        {
            return ResultadoEnvio.Procesado;
        }

        var cuerpo = new { eventos = lote.Select(e => e.Evento).ToList() };
        using var pedido = new HttpRequestMessage(HttpMethod.Post, new Uri(configuracion.UrlIngesta, "api/v1/eventos"))
        {
            // List<EventoVideo> con el tipo base: así el serializador escribe "tipo".
            Content = JsonContent.Create(cuerpo, options: OpcionesJson.Eventos),
        };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuracion.Token);

        var salida = reloj.GetUtcNow();
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.SendAsync(pedido, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Sin conexión con la ingesta ({Tipo}); {Pendientes} eventos en cola.", e.GetType().Name, cola.Pendientes);
            return ResultadoEnvio.Reintentar;
        }

        using (respuesta)
        {
            var llegada = reloj.GetUtcNow();
            if (respuesta.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge)
            {
                cola.Cuarentena(lote.Select(e => e.Archivo));
                log.LogError("La ingesta rechazó un lote entero ({Estado}); {Cantidad} eventos a cuarentena.",
                    (int)respuesta.StatusCode, lote.Count);
                return ResultadoEnvio.Cuarentena;
            }

            if (!respuesta.IsSuccessStatusCode)
            {
                log.LogWarning("La ingesta respondió {Estado}; se reintenta.", (int)respuesta.StatusCode);
                return ResultadoEnvio.Reintentar;
            }

            RespuestaIngesta? r;
            try
            {
                r = await respuesta.Content.ReadFromJsonAsync<RespuestaIngesta>(OpcionesRespuesta, ct);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException)
            {
                // Un 200 que no es la respuesta de la ingesta (portal cautivo, proxy):
                // no se sabe si llegó, así que no se borra nada y se reintenta.
                log.LogWarning("Respuesta 2xx que no es de la ingesta; se reintenta.");
                return ResultadoEnvio.Reintentar;
            }

            if (r is null)
            {
                return ResultadoEnvio.Reintentar;
            }

            // Punto medio del viaje contra la hora que informó el servidor. Positivo: el equipo adelanta.
            var medio = salida + (llegada - salida) / 2;
            estado.DesvioRelojMs = (long)(medio - r.HoraServidor).TotalMilliseconds;

            cola.Confirmar(lote.Select(e => e.Archivo));
            if (r.Rechazados.Count > 0)
            {
                // Solo índice y motivo: los motivos de la ingesta no citan contenido.
                foreach (var rechazo in r.Rechazados)
                {
                    log.LogError("Evento rechazado por la ingesta (índice {Indice}): {Motivos}",
                        rechazo.Indice, string.Join(" ", rechazo.Errores));
                }
            }

            log.LogInformation("Lote enviado: {Aceptados} aceptados, {Duplicados} duplicados, {Rechazados} rechazados.",
                r.Aceptados, r.Duplicados, r.Rechazados.Count);
            return ResultadoEnvio.Procesado;
        }
    }
}
