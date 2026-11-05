using System.Text.Json;
using Bravo.Video.Contratos;
using Bravo.Video.Patentes;

namespace Bravo.Video.Ingesta.Datos;

public static class Mapeo
{
    public static EventoGuardado AGuardado(EventoVideo evento, DateTime recibidoUtc, HashPatente hash)
    {
        var g = new EventoGuardado
        {
            EventId = evento.EventId,
            Tipo = TiposEvento.De(evento),
            SchemaVersion = evento.SchemaVersion,
            Sede = evento.Sede,
            Dispositivo = evento.Dispositivo,
            Camara = evento.Camara,
            TsUtc = evento.Ts.UtcDateTime,
            TsOffsetMinutos = (int)evento.Ts.Offset.TotalMinutes,
            RecibidoUtc = recibidoUtc,
            Motor = evento.Fuente.Motor,
            VersionMotor = evento.Fuente.Version,
        };

        switch (evento)
        {
            case EventoVehiculo v:
                g.IdOrigen = v.IdOrigen;
                if (v.Patente is { } p)
                {
                    g.PatentePais = p.Pais;
                    g.PatenteLeida = p.Leida;
                    g.PatenteNormalizada = p.Normalizada;
                    g.PatenteValida = p.Valida;
                    g.PatenteFormato = p.Formato;
                    g.PatenteCorrecciones = p.Correcciones;
                    g.PatenteConfianza = p.Confianza;

                    // Solo las válidas tienen hash: son las únicas que se emparejan.
                    if (p.Valida && Enum.TryParse<Pais>(p.Pais, out var pais))
                    {
                        g.PatenteHash = hash.Calcular(p.Normalizada!, pais);
                    }
                }

                break;
            case PersonasCruce c:
                g.Linea = c.Linea;
                g.Entradas = c.Entradas;
                g.Salidas = c.Salidas;
                g.DesdeUtc = c.Desde?.UtcDateTime;
                g.HastaUtc = c.Hasta?.UtcDateTime;
                break;
            case EventoBox b:
                g.Box = b.Box;
                g.IdOrigen = b.IdOrigen;
                break;
            case DispositivoLatido l:
                g.VersionConector = l.VersionConector;
                g.ColaPendiente = l.ColaPendiente;
                g.DesvioRelojMs = l.DesvioRelojMs;
                g.MotoresJson = JsonSerializer.Serialize(l.Motores, OpcionesJson.Eventos);
                break;
        }

        return g;
    }
}
