using System.Globalization;
using System.Security;
using System.Text.Json;
using Bravo.Video.Lectores;

namespace Bravo.Video.Simulacion;

/// <summary>Mensaje MQTT listo para publicar.</summary>
public sealed record MensajeMqtt(string Topico, string Carga);

/// <summary>
/// Arma los mensajes que mandaría cada motor para una pasada, con el mismo
/// formato que leen los lectores. Así el simulador prueba el conector real.
/// </summary>
public static class Mensajes
{
    public static string IdOrigen(Pasada pasada, string motor) => motor switch
    {
        Motores.Frigate => $"{pasada.Ts.ToUnixTimeSeconds()}.000000-sim{pasada.Clave}",
        Motores.HikvisionAnpr => $"sim-{pasada.Clave}",
        _ => throw new ArgumentOutOfRangeException(nameof(motor), motor, "Motor sin simulación."),
    };

    /// <summary>
    /// Frigate: "new", la lectura de patente por <c>tracked_object_update</c> (si
    /// hubo) y "end". El objeto pasa por la zona de ingreso o de egreso según la pasada.
    /// </summary>
    public static IReadOnlyList<MensajeMqtt> Frigate(Pasada pasada, string camara, string zonaIngreso, string zonaEgreso)
    {
        if (!pasada.Detectada)
        {
            return [];
        }

        var id = IdOrigen(pasada, Motores.Frigate);
        var inicio = pasada.Ts.ToUnixTimeMilliseconds() / 1000m;
        var zona = pasada.EsIngreso ? zonaIngreso : zonaEgreso;

        string Evento(string tipo, decimal? fin) => JsonSerializer.Serialize(new
        {
            type = tipo,
            before = new { },
            after = new
            {
                id,
                camera = camara,
                label = "car",
                start_time = inicio,
                end_time = fin,
                entered_zones = new[] { zona },
                current_zones = fin is null ? new[] { zona } : [],
                recognized_license_plate = (string?)null,
                recognized_license_plate_score = (decimal?)null,
            },
        });

        var mensajes = new List<MensajeMqtt> { new(LectorFrigate.TopicoEventos, Evento("new", null)) };
        if (pasada.PatenteLeida is { } patente)
        {
            mensajes.Add(new MensajeMqtt(LectorFrigate.TopicoActualizaciones, JsonSerializer.Serialize(new
            {
                type = "lpr",
                id,
                camera = camara,
                name = (string?)null,
                plate = patente,
                score = pasada.Confianza,
                timestamp = inicio + 1.5m,
            })));
        }

        mensajes.Add(new MensajeMqtt(LectorFrigate.TopicoEventos, Evento("end", inicio + 4m)));
        return mensajes;
    }

    /// <summary>Alerta XML de una cámara Hikvision ANPR. Null si la cámara no detectó el vehículo.</summary>
    public static string? Hikvision(Pasada pasada)
    {
        if (!pasada.Detectada)
        {
            return null;
        }

        var patente = SecurityElement.Escape(pasada.PatenteLeida ?? "unknown");
        var confianza = pasada.Confianza is { } c
            ? $"<confidenceLevel>{(c * 100m).ToString("0", CultureInfo.InvariantCulture)}</confidenceLevel>"
            : string.Empty;
        var fecha = pasada.Ts.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <EventNotificationAlert version="2.0" xmlns="http://www.isapi.org/ver20/XMLSchema">
              <ipAddress>192.0.2.20</ipAddress>
              <channelID>1</channelID>
              <dateTime>{fecha}</dateTime>
              <activePostCount>1</activePostCount>
              <eventType>ANPR</eventType>
              <eventState>active</eventState>
              <ANPR>
                <licensePlate>{patente}</licensePlate>
                <direction>{(pasada.EsIngreso ? "forward" : "reverse")}</direction>
                {confianza}
              </ANPR>
              <UUID>{IdOrigen(pasada, Motores.HikvisionAnpr)}</UUID>
            </EventNotificationAlert>
            """;
    }
}
