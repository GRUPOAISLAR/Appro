using Bravo.Video.Ingesta.Datos;
using Microsoft.EntityFrameworkCore;

namespace Bravo.Video.Ingesta;

public static class Base
{
    public static void Configurar(DbContextOptionsBuilder opciones, ConfiguracionIngesta configuracion)
    {
        switch (configuracion.Proveedor)
        {
            case ProveedorBase.SqlServer:
                opciones.UseSqlServer(configuracion.Conexion, sql => sql.EnableRetryOnFailure());
                break;
            case ProveedorBase.Sqlite:
                opciones.UseSqlite(configuracion.Conexion);
                break;
        }
    }

    /// <summary>
    /// Deja la base al día. En SQL Server aplica las migraciones; en SQLite (solo
    /// notebook y pruebas) crea las tablas desde el modelo, porque las migraciones
    /// están escritas para SQL Server.
    /// </summary>
    public static Task PrepararAsync(VideoDbContext db, ProveedorBase proveedor, CancellationToken ct = default) =>
        proveedor == ProveedorBase.SqlServer
            ? db.Database.MigrateAsync(ct)
            : db.Database.EnsureCreatedAsync(ct);
}
