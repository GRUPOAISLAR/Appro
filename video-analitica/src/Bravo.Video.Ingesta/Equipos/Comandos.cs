using Bravo.Video.Ingesta.Datos;
using Microsoft.EntityFrameworkCore;

namespace Bravo.Video.Ingesta.Equipos;

/// <summary>
/// Comandos de administración. Se corren en el servidor con las mismas variables
/// de entorno que la ingesta:
///
/// <code>
/// dotnet Bravo.Video.Ingesta.dll migrar
/// dotnet Bravo.Video.Ingesta.dll equipo-alta --dispositivo minipc-01 --sede AR-PRUEBA-01 [--reemplazar]
/// dotnet Bravo.Video.Ingesta.dll equipo-baja --dispositivo minipc-01
/// </code>
///
/// <c>equipo-alta</c> muestra el token una sola vez: se copia a la variable de
/// entorno del conector en el local y no queda guardado en ningún otro lado.
/// <c>--reemplazar</c> genera uno nuevo e invalida el anterior.
/// </summary>
public static class Comandos
{
    private static readonly string[] Nombres = ["migrar", "equipo-alta", "equipo-baja"];

    public static bool Existe(string nombre) => Nombres.Contains(nombre);

    public static async Task<int> EjecutarAsync(string[] args, TextWriter? salida = null, IConfiguration? config = null)
    {
        salida ??= Console.Out;
        config ??= new ConfigurationBuilder().AddEnvironmentVariables().Build();

        var configuracion = ConfiguracionIngesta.Leer(config);
        var opciones = new DbContextOptionsBuilder<VideoDbContext>();
        Base.Configurar(opciones, configuracion);
        await using var db = new VideoDbContext(opciones.Options);

        var parametros = Parametros(args);
        switch (args[0])
        {
            case "migrar":
                await Base.PrepararAsync(db, configuracion.Proveedor);
                await salida.WriteLineAsync("Base al día.");
                return 0;

            case "equipo-alta":
                return await AltaAsync(db, parametros, salida);

            case "equipo-baja":
            {
                if (!parametros.TryGetValue("dispositivo", out var dispositivo))
                {
                    await salida.WriteLineAsync("Falta --dispositivo.");
                    return 2;
                }

                var equipo = await db.Equipos.FindAsync(dispositivo);
                if (equipo is null)
                {
                    await salida.WriteLineAsync("No existe ese equipo.");
                    return 1;
                }

                equipo.Activo = false;
                await db.SaveChangesAsync();
                await salida.WriteLineAsync($"Equipo {dispositivo} dado de baja.");
                return 0;
            }

            default:
                return 2;
        }
    }

    private static async Task<int> AltaAsync(VideoDbContext db, Dictionary<string, string> parametros, TextWriter salida)
    {
        if (!parametros.TryGetValue("dispositivo", out var dispositivo) || !parametros.TryGetValue("sede", out var sede))
        {
            await salida.WriteLineAsync("Faltan --dispositivo y --sede.");
            return 2;
        }

        var token = Tokens.Generar();
        var existente = await db.Equipos.FindAsync(dispositivo);
        if (existente is not null && !parametros.ContainsKey("reemplazar"))
        {
            await salida.WriteLineAsync("El equipo ya existe. Usar --reemplazar para generar un token nuevo.");
            return 1;
        }

        if (existente is null)
        {
            db.Equipos.Add(new Equipo
            {
                Dispositivo = dispositivo,
                Sede = sede,
                TokenHash = Tokens.Hash(token),
                CreadoUtc = DateTime.UtcNow,
            });
        }
        else
        {
            existente.Sede = sede;
            existente.TokenHash = Tokens.Hash(token);
            existente.Activo = true;
        }

        await db.SaveChangesAsync();
        await salida.WriteLineAsync($"Equipo {dispositivo} ({sede}) listo. Token (se muestra una sola vez):");
        await salida.WriteLineAsync(token);
        return 0;
    }

    private static Dictionary<string, string> Parametros(string[] args)
    {
        var resultado = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var clave = args[i][2..];
            var tieneValor = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            resultado[clave] = tieneValor ? args[++i] : "true";
        }

        return resultado;
    }
}
