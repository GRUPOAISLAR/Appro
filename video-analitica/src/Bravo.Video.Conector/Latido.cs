using Bravo.Video.Conector.Cola;
using Bravo.Video.Contratos;
using Bravo.Video.Lectores;

namespace Bravo.Video.Conector;

/// <summary>
/// Encola un <see cref="DispositivoLatido"/> por minuto. Pasa por la misma cola
/// que el resto: si no hay internet, los latidos quedan guardados y al volver se
/// ve en el tablero cuánto duró el corte.
/// </summary>
public sealed class Latido(
    ColaDisco cola,
    ConfiguracionConector configuracion,
    EstadoConector estado,
    TimeProvider reloj,
    ILogger<Latido> log) : BackgroundService
{
    public static readonly string VersionConector =
        typeof(Latido).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromMinutes(1), reloj);
        do
        {
            try
            {
                if (!cola.Encolar(Armar()))
                {
                    log.LogError("Cola llena: no se pudo guardar el latido.");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.LogError("No se pudo escribir el latido en la cola ({Tipo}); ¿disco lleno?", e.GetType().Name);
            }

            if (estado.Descartes > 0 || estado.Invalidos > 0 || cola.RechazadosPorLlena > 0)
            {
                log.LogInformation(
                    "Desde el arranque: {Descartes} lecturas sin clasificar, {Invalidos} mensajes inválidos, {Llena} perdidos por cola llena.",
                    estado.Descartes, estado.Invalidos, cola.RechazadosPorLlena);
            }
        }
        while (await temporizador.WaitForNextTickAsync(ct));
    }

    public DispositivoLatido Armar()
    {
        var ahora = TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), configuracion.Sede.ZonaHoraria);

        // Un latido por minuto: el minuto es su identidad. Reencolar el mismo minuto no duplica.
        var minuto = ahora.ToUnixTimeSeconds() / 60;
        var motores = new List<EstadoMotor>();

        if (configuracion.Mqtt is not null)
        {
            motores.Add(new EstadoMotor("mqtt", estado.MqttConectado ? EstadosMotor.EnLinea : EstadosMotor.FueraDeLinea));
            motores.Add(new EstadoMotor(Motores.Frigate, estado.FrigateDisponible switch
            {
                true => EstadosMotor.EnLinea,
                false => EstadosMotor.FueraDeLinea,
                null => EstadosMotor.Desconocido,
            }));
        }

        foreach (var camara in configuracion.CamarasHttp.Keys.Order(StringComparer.Ordinal))
        {
            var nombre = $"{Motores.HikvisionAnpr}:{camara}";
            motores.Add(new EstadoMotor(nombre, estado.EstadoPorActividad(nombre, configuracion.VentanaActividad)));
        }

        foreach (var sensor in configuracion.Sensores.Keys.Order(StringComparer.Ordinal))
        {
            var nombre = $"{Motores.Milesight}:{sensor}";
            motores.Add(new EstadoMotor(nombre, estado.EstadoPorActividad(nombre, configuracion.VentanaActividad)));
        }

        return new DispositivoLatido
        {
            EventId = IdEvento.Derivar(configuracion.Sede.Sede, configuracion.Sede.Dispositivo, "conector",
                minuto.ToString(System.Globalization.CultureInfo.InvariantCulture), TiposEvento.DispositivoLatido),
            Sede = configuracion.Sede.Sede,
            Dispositivo = configuracion.Sede.Dispositivo,
            Ts = ahora,
            Fuente = new Fuente("conector", VersionConector),
            VersionConector = VersionConector,
            Motores = motores,
            ColaPendiente = cola.Pendientes,
            DesvioRelojMs = estado.DesvioRelojMs,
        };
    }
}
