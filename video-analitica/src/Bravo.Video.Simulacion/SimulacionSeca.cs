using Bravo.Video.Contratos;
using Bravo.Video.Evaluacion;
using Bravo.Video.Lectores;

namespace Bravo.Video.Simulacion;

/// <summary>
/// Pasa el escenario por los lectores y el armador reales, sin red ni procesos:
/// los mismos mensajes que publicaría el simulador, leídos por el mismo código que
/// usa el conector. Sirve para probar el evaluador y para ver rápido qué
/// porcentaje daría un nivel de ruido.
/// </summary>
public static class SimulacionSeca
{
    public static IReadOnlyList<EventoVehiculo> Ejecutar(IEnumerable<Pasada> pasadas, InstalacionSimulada instalacion, string versionMotor = "0.17.1")
    {
        var eventos = new List<EventoVehiculo>();
        var acumulador = new AcumuladorFrigate(versionMotor);

        foreach (var pasada in pasadas)
        {
            LecturaVehiculo? lectura = null;
            if (instalacion.Motor == Motores.Frigate)
            {
                foreach (var m in Mensajes.Frigate(pasada, instalacion.CamaraDe(pasada), instalacion.ZonaIngreso, instalacion.ZonaEgreso))
                {
                    if (m.Topico == LectorFrigate.TopicoActualizaciones)
                    {
                        acumulador.Procesar(LectorFrigate.LeerActualizacion(m.Carga)!, pasada.Ts);
                    }
                    else
                    {
                        lectura = acumulador.Procesar(LectorFrigate.LeerEvento(m.Carga), pasada.Ts) ?? lectura;
                    }
                }
            }
            else if (Mensajes.Hikvision(pasada) is { } xml)
            {
                lectura = LectorHikvisionAnpr.Leer(xml, instalacion.CamaraDe(pasada), "sim", instalacion.Sede.ZonaHoraria);
            }

            if (lectura is not null && ArmadorEventos.Vehiculo(lectura, instalacion.Sede).Evento is { } evento)
            {
                eventos.Add(evento);
            }
        }

        return eventos;
    }
}
