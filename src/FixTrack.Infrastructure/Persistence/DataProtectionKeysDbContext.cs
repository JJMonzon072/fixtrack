using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Persistence;

/// <summary>
/// Contexto exclusivo para el anillo de claves de ASP.NET Core Data Protection. Usa la tabla <c>DataProtectionKeys</c>,
/// cuyo esquema pertenece a las migraciones de <see cref="FixTrackDbContext"/>. Se registra con tiempos de espera cortos
/// y un solo reintento: Data Protection lee las claves de forma sincrónica, y con la configuración general (más tiempo y
/// más reintentos) una base que no responde retrasaría el arranque y bloquearía hilos en cada solicitud.
/// </summary>
public sealed class DataProtectionKeysDbContext(DbContextOptions<DataProtectionKeysDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys", table => table.ExcludeFromMigrations());
}
