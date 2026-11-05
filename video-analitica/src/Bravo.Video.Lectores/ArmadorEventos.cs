using Bravo.Video.Contratos;
using Bravo.Video.Patentes;

namespace Bravo.Video.Lectores;

public sealed record ResultadoArmado(EventoVehiculo? Evento, string? MotivoDescarte)
{
    public static ResultadoArmado Descartado(string motivo) => new(null, motivo);
}

/// <summary>
/// Convierte las lecturas de los motores en eventos del contrato v1: decide
/// ingreso o egreso según la configuración de la cámara, normaliza la patente,
/// pasa la hora a la zona de la sede y deriva el id del evento.
///
/// Una lectura que no se puede clasificar se descarta con motivo, nunca en
/// silencio: el conector cuenta los descartes y los informa en el latido.
/// </summary>
public static class ArmadorEventos
{
    public static ResultadoArmado Vehiculo(LecturaVehiculo lectura, ConfiguracionSede sede)
    {
        if (!sede.Camaras.TryGetValue(lectura.Camara, out var camara))
        {
            return ResultadoArmado.Descartado("Cámara sin configurar.");
        }

        if (camara.ZonaRequerida is { } zona && !Contiene(lectura.Zonas, zona))
        {
            return ResultadoArmado.Descartado("No pasó por la zona requerida de la cámara.");
        }

        if (Clasificar(lectura, camara) is not { } esIngreso)
        {
            return ResultadoArmado.Descartado(camara.Rol == RolCamara.PorZona
                ? "No pasó por una sola de las zonas de ingreso o egreso."
                : "Sentido de circulación no configurado.");
        }

        var tipo = esIngreso ? TiposEvento.VehiculoIngreso : TiposEvento.VehiculoEgreso;
        // La cámara entra en el id: el id de respaldo de la cámara ANPR (hora + texto)
        // podría repetirse entre dos cámaras que leen el mismo auto en el mismo instante.
        var id = IdEvento.Derivar(sede.Sede, sede.Dispositivo, $"{lectura.Motor}:{lectura.Camara}", lectura.IdOrigen, tipo);
        var ts = TimeZoneInfo.ConvertTime(lectura.Ts, sede.ZonaHoraria);
        var patente = lectura.PatenteLeida is { } leida ? Patente(leida, lectura.Confianza, sede.Pais) : null;
        var fuente = new Fuente(lectura.Motor, lectura.VersionMotor);

        EventoVehiculo evento = esIngreso
            ? new VehiculoIngreso
            {
                EventId = id, Sede = sede.Sede, Dispositivo = sede.Dispositivo, Camara = lectura.Camara,
                Ts = ts, Fuente = fuente, Patente = patente, IdOrigen = lectura.IdOrigen,
            }
            : new VehiculoEgreso
            {
                EventId = id, Sede = sede.Sede, Dispositivo = sede.Dispositivo, Camara = lectura.Camara,
                Ts = ts, Fuente = fuente, Patente = patente, IdOrigen = lectura.IdOrigen,
            };

        return new ResultadoArmado(evento, null);
    }

    /// <param name="sensor">Sensor que envió el reporte (lo identifica la URL que llamó).</param>
    /// <param name="recibido">Hora en que el conector recibió el reporte.</param>
    public static PersonasCruce Personas(
        ConteoLinea conteo, string sensor, string versionFirmware, DateTimeOffset recibido, ConfiguracionSede sede)
    {
        var ts = TimeZoneInfo.ConvertTime(recibido, sede.ZonaHoraria);
        var idOrigen = $"{conteo.Linea}@{recibido.ToUnixTimeMilliseconds()}";
        return new PersonasCruce
        {
            EventId = IdEvento.Derivar(sede.Sede, sede.Dispositivo, $"{Motores.Milesight}:{sensor}", idOrigen, TiposEvento.PersonasCruce),
            Sede = sede.Sede,
            Dispositivo = sede.Dispositivo,
            Camara = sensor,
            Ts = ts,
            Fuente = new Fuente(Motores.Milesight, versionFirmware),
            Linea = conteo.Linea,
            Entradas = conteo.Entradas,
            Salidas = conteo.Salidas,
        };
    }

    public static LecturaPatente Patente(string leida, decimal? confianza, Pais pais)
    {
        var r = NormalizadorPatentes.Normalizar(leida, pais);
        return new LecturaPatente
        {
            Leida = leida,
            Normalizada = r.Normalizada,
            Pais = pais.ToString(),
            Formato = r.Formato?.Nombre,
            Valida = r.Valida,
            Correcciones = r.Correcciones.Count,
            Confianza = confianza,
        };
    }

    /// <returns>true ingreso, false egreso, null si no se puede decidir.</returns>
    private static bool? Clasificar(LecturaVehiculo lectura, ConfiguracionCamara camara)
    {
        switch (camara.Rol)
        {
            case RolCamara.Ingreso:
                return true;
            case RolCamara.Egreso:
                return false;
            case RolCamara.PorZona:
            {
                var ingreso = Contiene(lectura.Zonas, camara.ZonaIngreso);
                var egreso = Contiene(lectura.Zonas, camara.ZonaEgreso);

                // Si pasó por las dos o por ninguna no se adivina.
                return ingreso == egreso ? null : ingreso;
            }

            case RolCamara.PorSentido:
                if (Igual(lectura.Sentido, camara.SentidoIngreso))
                {
                    return true;
                }

                if (Igual(lectura.Sentido, camara.SentidoEgreso))
                {
                    return false;
                }

                return null;
            default:
                return null;
        }
    }

    private static bool Contiene(IReadOnlyList<string> zonas, string? zona) =>
        zona is not null && zonas.Any(z => string.Equals(z, zona, StringComparison.OrdinalIgnoreCase));

    private static bool Igual(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
