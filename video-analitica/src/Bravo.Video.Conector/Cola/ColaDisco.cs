using System.Text.Json;
using Bravo.Video.Contratos;

namespace Bravo.Video.Conector.Cola;

public sealed record ElementoCola(string Archivo, EventoVideo Evento);

/// <summary>
/// Cola en disco: un archivo por evento, con nombre que ordena por llegada.
///
/// Por qué un archivo por evento y no una base: es lo más simple de inspeccionar y
/// reparar a mano en el local (se ve con <c>ls</c>), y la escritura es atómica
/// (archivo temporal + rename), así que un corte de luz deja el evento completo o
/// no lo deja. Con unos miles de eventos por día alcanza de sobra.
///
/// La cola tiene patentes en texto: vive en el disco del mini PC del local y se
/// borra a medida que la ingesta confirma. No se copia a ningún otro lado.
///
/// Si se llena, rechaza lo nuevo (no pisa lo viejo) y lo cuenta: el que mira el
/// latido ve que algo anda mal antes de perder datos que ya estaban.
/// </summary>
public sealed class ColaDisco
{
    private const string Extension = ".json";
    private const string Temporal = ".tmp";

    private readonly string _directorio;
    private readonly string _cuarentena;
    private readonly int _maximo;
    private readonly Lock _candado = new();
    private readonly SemaphoreSlim _hayNuevos = new(0);
    private long _secuencia;
    private int _pendientes;

    public ColaDisco(string directorio, int maximo)
    {
        _directorio = directorio;
        _cuarentena = Path.Combine(directorio, "cuarentena");
        _maximo = maximo;
        Directory.CreateDirectory(_directorio);
        Directory.CreateDirectory(_cuarentena);

        // Restos de una escritura cortada a la mitad: nunca llegaron a ser eventos.
        foreach (var tmp in Directory.EnumerateFiles(_directorio, "*" + Temporal))
        {
            File.Delete(tmp);
        }

        _pendientes = Directory.EnumerateFiles(_directorio, "*" + Extension).Count();
    }

    public int Pendientes => Volatile.Read(ref _pendientes);

    /// <summary>Eventos rechazados por cola llena desde que arrancó el conector.</summary>
    public long RechazadosPorLlena { get; private set; }

    public int EnCuarentena => Directory.EnumerateFiles(_cuarentena, "*" + Extension).Count();

    /// <returns>false si la cola está llena y el evento no se guardó.</returns>
    public bool Encolar(EventoVideo evento)
    {
        lock (_candado)
        {
            if (_pendientes >= _maximo)
            {
                RechazadosPorLlena++;
                return false;
            }

            // Ticks UTC + secuencia: ordena por llegada aunque dos eventos caigan en el mismo tick.
            var nombre = $"{DateTime.UtcNow.Ticks:D19}-{++_secuencia:D9}-{evento.EventId:N}";
            var temporal = Path.Combine(_directorio, nombre + Temporal);
            var final = Path.Combine(_directorio, nombre + Extension);

            File.WriteAllBytes(temporal, JsonSerializer.SerializeToUtf8Bytes(evento, OpcionesJson.Eventos));
            File.Move(temporal, final);
            _pendientes++;
        }

        _hayNuevos.Release();
        return true;
    }

    /// <summary>Los <paramref name="cantidad"/> eventos más viejos. Los ilegibles van a cuarentena.</summary>
    public IReadOnlyList<ElementoCola> Tomar(int cantidad)
    {
        lock (_candado)
        {
            var resultado = new List<ElementoCola>(cantidad);
            foreach (var archivo in Directory.EnumerateFiles(_directorio, "*" + Extension).Order(StringComparer.Ordinal))
            {
                if (resultado.Count == cantidad)
                {
                    break;
                }

                try
                {
                    var evento = JsonSerializer.Deserialize<EventoVideo>(File.ReadAllBytes(archivo), OpcionesJson.Eventos);
                    if (evento is not null)
                    {
                        resultado.Add(new ElementoCola(archivo, evento));
                        continue;
                    }
                }
                catch (JsonException)
                {
                }

                MoverACuarentenaSinCandado(archivo);
            }

            return resultado;
        }
    }

    /// <summary>Borra los eventos que la ingesta ya procesó (aceptados, duplicados o rechazados).</summary>
    public void Confirmar(IEnumerable<string> archivos)
    {
        lock (_candado)
        {
            foreach (var archivo in archivos)
            {
                if (File.Exists(archivo))
                {
                    File.Delete(archivo);
                    _pendientes--;
                }
            }
        }
    }

    /// <summary>
    /// Aparta eventos que la ingesta no acepta como lote (400) para que no frenen
    /// la cola para siempre. Quedan en disco para revisarlos a mano.
    /// </summary>
    public void Cuarentena(IEnumerable<string> archivos)
    {
        lock (_candado)
        {
            foreach (var archivo in archivos)
            {
                MoverACuarentenaSinCandado(archivo);
            }
        }
    }

    /// <summary>Espera a que haya algo nuevo o a que pase <paramref name="maximo"/>.</summary>
    public async Task EsperarAsync(TimeSpan maximo, CancellationToken ct)
    {
        await _hayNuevos.WaitAsync(maximo, ct);
    }

    private void MoverACuarentenaSinCandado(string archivo)
    {
        if (!File.Exists(archivo))
        {
            return;
        }

        File.Move(archivo, Path.Combine(_cuarentena, Path.GetFileName(archivo)), overwrite: true);
        _pendientes--;
    }
}
