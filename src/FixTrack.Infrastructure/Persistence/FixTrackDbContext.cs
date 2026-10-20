using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Persistence;

/// <summary>Contexto de Entity Framework Core de FixTrack (SQL Server).</summary>
public sealed class FixTrackDbContext(DbContextOptions<FixTrackDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<Technician> Technicians => Set<Technician>();

    public DbSet<ServiceOrder> ServiceOrders => Set<ServiceOrder>();

    public DbSet<ServiceOrderStatusChange> ServiceOrderStatusHistory => Set<ServiceOrderStatusChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FixTrackDbContext).Assembly);

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
