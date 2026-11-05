using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bravo.Video.Ingesta.Datos;

public sealed class VideoDbContext(DbContextOptions<VideoDbContext> opciones) : DbContext(opciones)
{
    public DbSet<Equipo> Equipos => Set<Equipo>();
    public DbSet<EventoGuardado> Eventos => Set<EventoGuardado>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.Entity<Equipo>(e =>
        {
            e.ToTable("Equipos");
            e.HasKey(x => x.Dispositivo);
            e.Property(x => x.Dispositivo).HasMaxLength(64);
            e.Property(x => x.Sede).HasMaxLength(64);
            e.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength();
            e.HasIndex(x => x.TokenHash).IsUnique();
        });

        modelo.Entity<EventoGuardado>(e =>
        {
            e.ToTable("Eventos");
            // El dispositivo entra en la clave: un equipo no puede "ocupar" el id de un
            // evento de otro equipo (los ids son predecibles) y hacerlo pasar por duplicado.
            e.HasKey(x => new { x.Dispositivo, x.EventId });
            e.Property(x => x.EventId).ValueGeneratedNever();
            e.Property(x => x.Tipo).HasMaxLength(32);
            e.Property(x => x.SchemaVersion).HasMaxLength(8);
            e.Property(x => x.Sede).HasMaxLength(64);
            e.Property(x => x.Dispositivo).HasMaxLength(64);
            e.Property(x => x.Camara).HasMaxLength(64);
            e.Property(x => x.Motor).HasMaxLength(64);
            e.Property(x => x.VersionMotor).HasMaxLength(64);
            e.Property(x => x.IdOrigen).HasMaxLength(128);
            e.Property(x => x.PatentePais).HasMaxLength(2);
            e.Property(x => x.PatenteLeida).HasMaxLength(16);
            e.Property(x => x.PatenteNormalizada).HasMaxLength(16);
            e.Property(x => x.PatenteHash).HasMaxLength(67);
            e.Property(x => x.PatenteFormato).HasMaxLength(32);

            // Confianza entre 0 y 1: cuatro decimales es más de lo que informa cualquier motor.
            e.Property(x => x.PatenteConfianza).HasPrecision(5, 4);
            e.Property(x => x.Linea).HasMaxLength(64);
            e.Property(x => x.Box).HasMaxLength(64);
            e.Property(x => x.VersionConector).HasMaxLength(64);
            e.Property(x => x.MotoresJson).HasMaxLength(2000);

            e.HasIndex(x => new { x.Sede, x.TsUtc });
            e.HasIndex(x => new { x.Dispositivo, x.TsUtc });
            e.HasIndex(x => x.PatenteHash);

            // La tarea de anonimización busca por esto.
            e.HasIndex(x => new { x.PatenteAnonimizadaUtc, x.TsUtc });
        });
    }
}

/// <summary>
/// Solo para <c>dotnet ef migrations add</c>. Las migraciones son de SQL Server; la
/// cadena no se usa para conectarse y no es una credencial.
/// </summary>
public sealed class FabricaDisenio : IDesignTimeDbContextFactory<VideoDbContext>
{
    public VideoDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<VideoDbContext>()
            .UseSqlServer("Server=localhost;Database=VideoAnalitica;Integrated Security=true;TrustServerCertificate=true")
            .Options);
}
