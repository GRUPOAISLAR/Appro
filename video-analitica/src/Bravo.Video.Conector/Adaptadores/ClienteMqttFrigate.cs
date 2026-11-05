using System.Buffers;
using MQTTnet;

namespace Bravo.Video.Conector.Adaptadores;

/// <summary>
/// Conexión con Mosquitto (MQTTnet 5.2). Se reconecta sola cada 5 segundos si se
/// corta, y se vuelve a suscribir en cada conexión.
///
/// Sesión limpia a propósito: si el conector estuvo caído, los mensajes de ese
/// rato se pierden en vez de llegar todos juntos fuera de orden. Lo que pasó
/// mientras tanto queda en la grabación de Frigate, no en la ingesta.
/// </summary>
public sealed class ClienteMqttFrigate(
    ProcesadorFrigate procesador,
    ConfiguracionConector configuracion,
    EstadoConector estado,
    TimeProvider reloj,
    ILogger<ClienteMqttFrigate> log) : BackgroundService
{
    private static readonly TimeSpan EsperaReconexion = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (configuracion.Mqtt is not { } mqtt)
        {
            log.LogInformation("MQTT sin configurar: Frigate desactivado.");
            return;
        }

        var fabrica = new MqttClientFactory();
        using var cliente = fabrica.CreateMqttClient();
        cliente.ApplicationMessageReceivedAsync += e =>
        {
            procesador.Procesar(e.ApplicationMessage.Topic, e.ApplicationMessage.Payload.ToArray());
            return Task.CompletedTask;
        };
        cliente.DisconnectedAsync += _ =>
        {
            estado.MqttConectado = false;
            return Task.CompletedTask;
        };

        var opciones = new MqttClientOptionsBuilder()
            .WithTcpServer(mqtt.Host, mqtt.Puerto)
            .WithClientId($"bravo-conector-{configuracion.Sede.Dispositivo}")
            .WithCleanSession()
            .WithCredentials(mqtt.Usuario, mqtt.Clave)
            .Build();

        var suscripcion = fabrica.CreateSubscribeOptionsBuilder();
        foreach (var topico in ProcesadorFrigate.Topicos)
        {
            suscripcion.WithTopicFilter(topico);
        }

        var opcionesSuscripcion = suscripcion.Build();
        using var purga = new PeriodicTimer(TimeSpan.FromMinutes(1), reloj);
        var proximaPurga = purga.WaitForNextTickAsync(ct).AsTask();

        while (!ct.IsCancellationRequested)
        {
            if (!cliente.IsConnected)
            {
                try
                {
                    await cliente.ConnectAsync(opciones, ct);
                    await cliente.SubscribeAsync(opcionesSuscripcion, ct);
                    estado.MqttConectado = true;
                    log.LogInformation("Conectado a MQTT en {Host}:{Puerto}.", mqtt.Host, mqtt.Puerto);
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    estado.MqttConectado = false;
                    log.LogWarning("No se pudo conectar a MQTT ({Tipo}); reintento en 5 s.", e.GetType().Name);
                }
            }

            if (proximaPurga.IsCompleted)
            {
                var purgados = procesador.Purgar();
                if (purgados > 0)
                {
                    log.LogInformation("{Cantidad} objetos de Frigate sin fin descartados.", purgados);
                }

                proximaPurga = purga.WaitForNextTickAsync(ct).AsTask();
            }

            try
            {
                await Task.Delay(EsperaReconexion, reloj, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (cliente.IsConnected)
        {
            await cliente.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None);
        }
    }
}
