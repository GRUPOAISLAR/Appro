using System.Collections.Concurrent;
using Bravo.Video.Contratos;

namespace Bravo.Video.Conector;

/// <summary>
/// Lo que el latido informa: estado de cada motor y desvío del reloj. Lo escriben
/// los adaptadores y el enviador; lo lee el latido.
/// </summary>
public sealed class EstadoConector(TimeProvider reloj)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _ultimoMensaje = new(StringComparer.Ordinal);
    private long _descartes;
    private long _invalidos;

    /// <summary>true si Frigate publicó "online", false si "offline", null si no se sabe.</summary>
    public bool? FrigateDisponible { get; set; }

    public bool MqttConectado { get; set; }

    /// <summary>Último desvío medido contra la hora del servidor. Null hasta el primer envío exitoso.</summary>
    public long? DesvioRelojMs { get; set; }

    public long Descartes => Interlocked.Read(ref _descartes);
    public long Invalidos => Interlocked.Read(ref _invalidos);

    public void MensajeRecibido(string origen) => _ultimoMensaje[origen] = reloj.GetUtcNow();

    public void Descarte() => Interlocked.Increment(ref _descartes);

    public void Invalido() => Interlocked.Increment(ref _invalidos);

    /// <summary>
    /// Estado de un equipo que solo manda cuando hay tránsito: en línea si mandó
    /// algo dentro de la ventana; si no, desconocido (no se puede distinguir
    /// "caído" de "no pasó nadie").
    /// </summary>
    public string EstadoPorActividad(string origen, TimeSpan ventana) =>
        _ultimoMensaje.TryGetValue(origen, out var ultimo) && reloj.GetUtcNow() - ultimo <= ventana
            ? EstadosMotor.EnLinea
            : EstadosMotor.Desconocido;
}
