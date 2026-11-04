using Bravo.Video.Ingesta.Datos;
using Bravo.Video.Ingesta.Equipos;
using Microsoft.EntityFrameworkCore;

namespace Bravo.Video.Ingesta.Api;

public static class Autenticacion
{
    /// <summary>Equipo activo dueño del token del encabezado <c>Authorization: Bearer</c>, o null.</summary>
    public static async Task<Equipo?> EquipoAsync(HttpRequest pedido, VideoDbContext db, CancellationToken ct)
    {
        var encabezado = pedido.Headers.Authorization.ToString();
        const string esquema = "Bearer ";
        if (!encabezado.StartsWith(esquema, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = encabezado[esquema.Length..].Trim();
        if (!token.StartsWith(Tokens.Prefijo, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = Tokens.Hash(token);
        return await db.Equipos.SingleOrDefaultAsync(e => e.TokenHash == hash && e.Activo, ct);
    }
}
