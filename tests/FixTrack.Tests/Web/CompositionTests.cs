using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Modules;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web;

/// <summary>Verifica la configuración de inyección de dependencias de la aplicación.</summary>
public sealed class CompositionTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    [Fact]
    public void Services_from_every_layer_are_registered()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.NotNull(services.GetService<IModuleCatalog>());   // Aplicación
        Assert.NotNull(services.GetService<ICustomerService>());  // Aplicación
        Assert.NotNull(services.GetService<IUnitOfWork>());       // Infraestructura
        Assert.Same(TimeProvider.System, services.GetService<TimeProvider>());   // Infraestructura
        Assert.NotNull(services.GetService<ApplicationInfo>());   // Presentación
    }

    [Fact]
    public void Automated_tests_never_use_the_real_sql_server_database()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FixTrackDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
        Assert.Contains(":memory:", context.Database.GetConnectionString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dashboard_shows_the_current_date_from_the_injected_clock()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero));
        using var client = await factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(clock)))
            .CreateAuthenticatedClientAsync();

        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        Assert.Contains("Lunes, 19 de octubre de 2026", html);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
