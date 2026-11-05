namespace Bravo.Video.Ingesta;

public enum ProveedorBase
{
    SqlServer,

    /// <summary>Solo para correr en una notebook o en pruebas. No para la prueba en la empresa.</summary>
    Sqlite,
}

/// <summary>
/// Configuración del servidor, toda por variables de entorno. Nada de esto va en
/// <c>appsettings.json</c> ni en el repo.
///
/// No hay valores por defecto para lo que es decisión del Owner (plazo de
/// retención) ni para los secretos: si falta algo, el servidor no arranca y dice
/// qué falta, sin mostrar valores.
/// </summary>
public sealed record ConfiguracionIngesta
{
    public const string VarProveedor = "VIDEO_DB_PROVEEDOR";
    public const string VarConexion = "VIDEO_DB_CONEXION";
    public const string VarClaveHash = "VIDEO_CLAVE_HASH_PATENTE";
    public const string VarRetencionDias = "VIDEO_RETENCION_PATENTE_DIAS";
    public const string VarIntervaloMinutos = "VIDEO_ANONIMIZACION_INTERVALO_MINUTOS";

    public const int MaximoEventosPorLote = 500;

    public required ProveedorBase Proveedor { get; init; }
    public required string Conexion { get; init; }
    public required byte[] ClaveHashPatente { get; init; }

    /// <summary>Cuánto tiempo se guarda la patente en texto antes de dejar solo el hash.</summary>
    public required TimeSpan RetencionPatente { get; init; }

    /// <summary>Cada cuánto corre la tarea de anonimización. Operativo, no de negocio: por defecto 60 minutos.</summary>
    public TimeSpan IntervaloAnonimizacion { get; init; } = TimeSpan.FromMinutes(60);

    public static ConfiguracionIngesta Leer(IConfiguration configuracion)
    {
        var errores = new List<string>();

        var textoProveedor = configuracion[VarProveedor];
        if (!Enum.TryParse<ProveedorBase>(textoProveedor, ignoreCase: true, out var proveedor))
        {
            errores.Add($"{VarProveedor} tiene que ser SqlServer o Sqlite.");
        }

        var conexion = configuracion[VarConexion];
        if (string.IsNullOrWhiteSpace(conexion))
        {
            errores.Add($"{VarConexion} vacía.");
        }

        byte[] clave = [];
        try
        {
            clave = Convert.FromBase64String(configuracion[VarClaveHash] ?? string.Empty);
        }
        catch (FormatException)
        {
            // El valor no se muestra: es un secreto.
        }

        if (clave.Length < Patentes.HashPatente.LargoMinimoClave)
        {
            errores.Add($"{VarClaveHash} tiene que ser base64 de al menos {Patentes.HashPatente.LargoMinimoClave} bytes.");
        }

        if (!int.TryParse(configuracion[VarRetencionDias], out var dias) || dias < 1)
        {
            errores.Add($"{VarRetencionDias} tiene que ser un entero de días mayor o igual a 1 (lo define el Owner).");
        }

        var intervalo = TimeSpan.FromMinutes(60);
        if (configuracion[VarIntervaloMinutos] is { } textoIntervalo)
        {
            if (!int.TryParse(textoIntervalo, out var minutos) || minutos < 1)
            {
                errores.Add($"{VarIntervaloMinutos} tiene que ser un entero de minutos mayor o igual a 1.");
            }
            else
            {
                intervalo = TimeSpan.FromMinutes(minutos);
            }
        }

        if (errores.Count > 0)
        {
            throw new InvalidOperationException("Configuración incompleta: " + string.Join(" ", errores));
        }

        return new ConfiguracionIngesta
        {
            Proveedor = proveedor,
            Conexion = conexion!,
            ClaveHashPatente = clave,
            RetencionPatente = TimeSpan.FromDays(dias),
            IntervaloAnonimizacion = intervalo,
        };
    }
}
