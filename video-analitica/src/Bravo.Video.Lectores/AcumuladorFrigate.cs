namespace Bravo.Video.Lectores;

/// <summary>
/// Junta los mensajes de un mismo objeto de Frigate hasta que termina, y entonces
/// entrega una sola lectura.
///
/// Por qué se espera al final: mientras el auto está en cuadro, Frigate puede
/// mandar varias lecturas de la patente y mejorar la confianza en cada una. Si se
/// emitiera en la primera, quedaría la peor. Al terminar el objeto se queda la
/// lectura de mayor puntaje entre la del evento y las de
/// <c>tracked_object_update</c>; si empatan, la que llegó primero.
///
/// Se emite una lectura aunque no haya patente: el vehículo cuenta igual para el
/// conteo de ingresos y egresos.
/// </summary>
public sealed class AcumuladorFrigate
{
    public static readonly IReadOnlySet<string> EtiquetasVehiculo =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "car", "motorcycle", "truck", "bus" };

    private readonly string _versionFrigate;
    private readonly Dictionary<string, Estado> _porId = new(StringComparer.Ordinal);

    public AcumuladorFrigate(string versionFrigate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionFrigate);
        _versionFrigate = versionFrigate;
    }

    /// <summary>Objetos abiertos (sin mensaje de fin todavía).</summary>
    public int Abiertos => _porId.Count;

    /// <summary>Devuelve la lectura cuando llega el fin de un vehículo; si no, null.</summary>
    public LecturaVehiculo? Procesar(MensajeEventoFrigate mensaje, DateTimeOffset recibido)
    {
        if (!EtiquetasVehiculo.Contains(mensaje.Etiqueta))
        {
            _porId.Remove(mensaje.Id);
            return null;
        }

        var estado = Obtener(mensaje.Id, mensaje.Camara, recibido);
        estado.Inicio = mensaje.Inicio;
        estado.Zonas.UnionWith(mensaje.ZonasIngresadas);
        if (mensaje.Patente is not null)
        {
            estado.Considerar(mensaje.Patente, mensaje.PuntajePatente);
        }

        if (mensaje.Tipo != "end")
        {
            return null;
        }

        _porId.Remove(mensaje.Id);
        return new LecturaVehiculo
        {
            Motor = Motores.Frigate,
            VersionMotor = _versionFrigate,
            Camara = estado.Camara,
            IdOrigen = mensaje.Id,
            Ts = estado.Inicio ?? mensaje.Inicio,
            PatenteLeida = estado.Patente,
            Confianza = estado.Puntaje,
            Zonas = estado.Zonas.Order(StringComparer.Ordinal).ToList(),
        };
    }

    public void Procesar(ActualizacionPatenteFrigate actualizacion, DateTimeOffset recibido)
    {
        var estado = Obtener(actualizacion.Id, actualizacion.Camara, recibido);
        estado.Considerar(actualizacion.Patente, actualizacion.Puntaje);
    }

    /// <summary>
    /// Descarta objetos cuyo fin nunca llegó (Frigate reiniciado, mensaje perdido)
    /// y que no tienen actividad desde <paramref name="antesDe"/>. Devuelve cuántos.
    /// </summary>
    public int Purgar(DateTimeOffset antesDe)
    {
        var viejos = _porId.Where(p => p.Value.UltimaActividad < antesDe).Select(p => p.Key).ToList();
        foreach (var id in viejos)
        {
            _porId.Remove(id);
        }

        return viejos.Count;
    }

    private Estado Obtener(string id, string camara, DateTimeOffset recibido)
    {
        if (!_porId.TryGetValue(id, out var estado))
        {
            estado = new Estado(camara);
            _porId[id] = estado;
        }

        estado.UltimaActividad = recibido;
        return estado;
    }

    private sealed class Estado(string camara)
    {
        public string Camara { get; } = camara;
        public DateTimeOffset? Inicio { get; set; }
        public HashSet<string> Zonas { get; } = new(StringComparer.Ordinal);
        public string? Patente { get; private set; }
        public decimal? Puntaje { get; private set; }
        public DateTimeOffset UltimaActividad { get; set; }

        /// <summary>Reemplaza solo si mejora estrictamente el puntaje (sin puntaje cuenta como el peor).</summary>
        public void Considerar(string patente, decimal? puntaje)
        {
            if (Patente is null || (puntaje ?? -1m) > (Puntaje ?? -1m))
            {
                Patente = patente;
                Puntaje = puntaje;
            }
        }
    }
}
