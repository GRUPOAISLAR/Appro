namespace Bravo.Video.Estadias;

/// <summary>
/// Promedio y mediana de duración, en segundos.
///
/// Se calcula en <c>decimal</c> sobre los ticks exactos de cada estadía. La única
/// pérdida posible es la de la división del promedio, a 28 dígitos significativos.
/// No se redondea a minutos acá: eso es decisión de quien lo muestra.
/// </summary>
public sealed record ResumenEstadias(int Cantidad, decimal? PromedioSegundos, decimal? MedianaSegundos)
{
    public static ResumenEstadias Calcular(IEnumerable<Estadia> estadias)
    {
        var segundos = estadias
            .Select(e => (decimal)e.Duracion.Ticks / TimeSpan.TicksPerSecond)
            .Order()
            .ToList();

        if (segundos.Count == 0)
        {
            return new ResumenEstadias(0, null, null);
        }

        var promedio = segundos.Sum() / segundos.Count;
        var medio = segundos.Count / 2;
        var mediana = segundos.Count % 2 == 1
            ? segundos[medio]
            : (segundos[medio - 1] + segundos[medio]) / 2m;

        return new ResumenEstadias(segundos.Count, promedio, mediana);
    }
}
