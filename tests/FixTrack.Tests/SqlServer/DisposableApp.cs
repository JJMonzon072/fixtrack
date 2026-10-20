using FixTrack.Infrastructure.Persistence;
using FixTrack.Tests.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FixTrack.Tests.SqlServer;

/// <summary>
/// La aplicación real (servicios, Identity y estrategia de reintentos) sobre la base desechable. El entorno "Pruebas" no
/// carga secretos de usuario y los contextos se reemplazan explícitamente: aunque la máquina tenga configurada otra base,
/// estas pruebas solo pueden escribir en la desechable, y se comprueba antes de devolver la aplicación.
/// </summary>
internal static class DisposableApp
{
    public static async Task<WebApplicationFactory<Program>> CreateAsync(string connectionString)
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Pruebas")
            .UseSetting("ConnectionStrings:FixTrack", connectionString)
            .UseProtectedKeysInDatabase()
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<FixTrackDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<IDbContextOptionsConfiguration<FixTrackDbContext>>();
                services.RemoveAll<FixTrackDbContext>();
                services.AddDbContext<FixTrackDbContext>(options => options.UseSqlServer(
                    connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));
                services.RemoveAll<DbContextOptions<DataProtectionKeysDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<DataProtectionKeysDbContext>>();
                services.RemoveAll<DataProtectionKeysDbContext>();
                services.AddDbContext<DataProtectionKeysDbContext>(options => options.UseSqlServer(connectionString));
            }));

        await using var scope = app.Services.CreateAsyncScope();
        var catalog = new SqlConnectionStringBuilder(scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().Database.GetConnectionString())
            .InitialCatalog;
        if (!catalog.StartsWith("FixTrackPrueba_", StringComparison.Ordinal))
        {
            await app.DisposeAsync();
            throw new InvalidOperationException($"Estas pruebas solo se ejecutan en una base desechable; se obtuvo «{catalog}».");
        }

        return app;
    }
}
