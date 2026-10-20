using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;
using FixTrack.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Persistence;

/// <summary>
/// Contexto de Entity Framework Core de FixTrack (SQL Server). Incluye las tablas de ASP.NET Core Identity
/// (cuentas y roles) y las claves de Data Protection, que cifran las cookies de sesión y los tokens antifalsificación.
/// </summary>
public sealed class FixTrackDbContext(DbContextOptions<FixTrackDbContext> options)
    : IdentityDbContext<FixTrackUser, IdentityRole, string>(options), IDataProtectionKeyContext
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<Technician> Technicians => Set<Technician>();

    public DbSet<ServiceOrder> ServiceOrders => Set<ServiceOrder>();

    public DbSet<ServiceOrderStatusChange> ServiceOrderStatusHistory => Set<ServiceOrderStatusChange>();

    public DbSet<ServiceOrderAssignment> ServiceOrderAssignments => Set<ServiceOrderAssignment>();

    /// <summary>
    /// Claves de Data Protection. Guardarlas en la base permite que las sesiones sobrevivan a reinicios y
    /// redespliegues del contenedor (por ejemplo, en Render).
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Primero el modelo de Identity; después las configuraciones propias, que lo complementan.
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FixTrackDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Todas las fechas se guardan en UTC (datetime2 con milisegundos) y se leen marcadas como UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HavePrecision(3);
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>().HavePrecision(3);

        // Montos monetarios: hasta 99 999 999.99.
        configurationBuilder.Properties<decimal>().HavePrecision(10, 2);
        configurationBuilder.Properties<decimal?>().HavePrecision(10, 2);
    }
}
