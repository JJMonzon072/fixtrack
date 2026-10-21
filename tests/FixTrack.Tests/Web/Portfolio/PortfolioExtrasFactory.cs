using FixTrack.Infrastructure.Persistence;
using FixTrack.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FixTrack.Tests.Web.Portfolio;

/// <summary>
/// FixTrack en memoria con los tres extras de portafolio habilitados (solo en las pruebas), un reloj fijo en hora de
/// Guatemala (3 de noviembre de 2026, 12:00 local) y un máximo de exportación bajo para probar el límite.
/// </summary>
public sealed class PortfolioExtrasFactory : FixTrackWebApplicationFactory
{
    public static readonly DateTimeOffset Now = new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);

    /// <summary>Menor que las 7 órdenes recibidas en octubre y mayor que las 4 entregadas.</summary>
    public const int ExportMaxRows = 6;

    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private bool _seeded;

    protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings =>
    [
        new("PortfolioFeatures:Reports", "true"),
        new("PortfolioFeatures:PrintableSheets", "true"),
        new("PortfolioFeatures:ReadOnlyApi", "true"),
        new("PortfolioFeatures:ExportMaxRows", ExportMaxRows.ToString(System.Globalization.CultureInfo.InvariantCulture)),
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new GuatemalaClock(Now));
        });
    }

    /// <summary>Carga una sola vez el escenario de <see cref="ReportSeed"/> (octubre de 2026).</summary>
    public async Task SeedReportsAsync()
    {
        await _seedLock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (_seeded)
            {
                return;
            }

            await using var scope = Services.CreateAsyncScope();
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<FixTrackDbContext>>();
            await ReportSeed.SeedAsync(() => new FixTrackDbContext(options), TestContext.Current.CancellationToken);
            _seeded = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _seedLock.Dispose();
        }
    }
}

/// <summary>Reloj fijo con la zona America/Guatemala, para que los períodos no dependan de la máquina.</summary>
public sealed class GuatemalaClock(DateTimeOffset now) : TimeProvider
{
    public static readonly TimeZoneInfo Guatemala = TimeZoneInfo.FindSystemTimeZoneById("America/Guatemala");

    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => Guatemala;
}
