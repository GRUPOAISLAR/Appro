using System.Text;
using Bravo.Video.Conector.Cola;
using Bravo.Video.Lectores;

namespace Bravo.Video.Conector.Adaptadores;

/// <summary>
/// Recibe los mensajes MQTT de Frigate, ya separados del cliente MQTT para poder
/// probarlo sin broker, y encola los eventos resultantes.
/// </summary>
public sealed class ProcesadorFrigate(
    ColaDisco cola,
    ConfiguracionConector configuracion,
    EstadoConector estado,
    TimeProvider reloj,
    ILogger<ProcesadorFrigate> log)
{
    private readonly AcumuladorFrigate _acumulador = new(configuracion.Mqtt?.VersionFrigate ?? "desconocida");
    private readonly Lock _candado = new();

    public static readonly string[] Topicos =
        [LectorFrigate.TopicoEventos, LectorFrigate.TopicoActualizaciones, LectorFrigate.TopicoDisponible];

    public int Abiertos
    {
        get
        {
            lock (_candado)
            {
                return _acumulador.Abiertos;
            }
        }
    }

    public void Procesar(string topico, ReadOnlySpan<byte> carga)
    {
        var texto = Encoding.UTF8.GetString(carga);
        var ahora = reloj.GetUtcNow();
        try
        {
            switch (topico)
            {
                case LectorFrigate.TopicoDisponible:
                    estado.FrigateDisponible = LectorFrigate.LeerDisponibilidad(texto);
                    return;

                case LectorFrigate.TopicoActualizaciones:
                    if (LectorFrigate.LeerActualizacion(texto) is { } actualizacion)
                    {
                        lock (_candado)
                        {
                            _acumulador.Procesar(actualizacion, ahora);
                        }
                    }

                    return;

                case LectorFrigate.TopicoEventos:
                    var mensaje = LectorFrigate.LeerEvento(texto);
                    LecturaVehiculo? lectura;
                    lock (_candado)
                    {
                        lectura = _acumulador.Procesar(mensaje, ahora);
                    }

                    if (lectura is not null)
                    {
                        Encolar(lectura);
                    }

                    return;
            }
        }
        catch (MensajeInvalidoException e)
        {
            // El mensaje de la excepción no trae contenido; el payload no se loguea.
            estado.Invalido();
            log.LogWarning("Mensaje de Frigate descartado en {Topico}: {Motivo}", topico, e.Message);
        }
    }

    /// <summary>Descarta objetos sin mensaje de fin que llevan más de 10 minutos sin actividad.</summary>
    public int Purgar()
    {
        lock (_candado)
        {
            return _acumulador.Purgar(reloj.GetUtcNow() - TimeSpan.FromMinutes(10));
        }
    }

    private void Encolar(LecturaVehiculo lectura)
    {
        estado.MensajeRecibido($"{Motores.Frigate}:{lectura.Camara}");
        var armado = ArmadorEventos.Vehiculo(lectura, configuracion.Sede);
        if (armado.Evento is null)
        {
            estado.Descarte();
            log.LogInformation("Lectura de Frigate descartada en {Camara}: {Motivo}", lectura.Camara, armado.MotivoDescarte);
            return;
        }

        if (!cola.Encolar(armado.Evento))
        {
            log.LogError("Cola llena: se perdió un evento de {Camara}.", lectura.Camara);
        }
    }
}
