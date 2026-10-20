using FixTrack.Infrastructure.Persistence;
using FixTrack.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FixTrack.Tests.Web;

/// <summary>
/// Levanta FixTrack en memoria con una base SQLite propia. El contexto de SQL Server se elimina
/// por completo, de modo que las pruebas nunca pueden conectarse a la base de datos real.
/// </summary>
public sealed class FixTrackWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = SqliteTestDatabase.CreateOpenConnection();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Valor ficticio: solo evita que la aplicación se detenga por falta de configuración.
        builder.UseSetting("ConnectionStrings:FixTrack", "Server=pruebas-sin-base-real;Database=NoUsar;");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<FixTrackDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<FixTrackDbContext>>();
            services.RemoveAll<FixTrackDbContext>();
            services.AddDbContext<FixTrackDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().Database.EnsureCreated();
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
