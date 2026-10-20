using FixTrack.Application.Security;
using FixTrack.Infrastructure.Identity;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FixTrack.Tests.Web;

/// <summary>
/// Levanta FixTrack en memoria con una base SQLite propia. El contexto de SQL Server se elimina
/// por completo, de modo que las pruebas nunca pueden conectarse a la base de datos real.
/// Crea las cuentas de <see cref="TestAccounts"/>, con contraseñas aleatorias generadas en cada ejecución.
/// </summary>
public sealed class FixTrackWebApplicationFactory : WebApplicationFactory<Program>
{
    // El esquema se crea antes de iniciar la aplicación: Data Protection lee sus claves de la base al arrancar.
    private readonly SqliteTestDatabase _database = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Valor ficticio: solo evita que la aplicación se detenga por falta de configuración.
        builder.UseSetting("ConnectionStrings:FixTrack", "Server=pruebas-sin-base-real;Database=NoUsar;");

        // Muchas pruebas inician sesión desde la misma "dirección": el límite real se prueba aparte.
        builder.UseSetting("Security:LoginAttemptsPerMinute", "10000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<FixTrackDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<FixTrackDbContext>>();
            services.RemoveAll<FixTrackDbContext>();
            services.AddDbContext<FixTrackDbContext>(options => options.UseSqlite(_database.Connection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        SeedAccountsAsync(scope.ServiceProvider.GetRequiredService<UserManager<FixTrackUser>>()).GetAwaiter().GetResult();
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _database.Dispose();
        }
    }

    private static async Task SeedAccountsAsync(UserManager<FixTrackUser> users)
    {
        foreach (var account in TestAccounts.All)
        {
            if (await users.FindByEmailAsync(account.Email) is not null)
            {
                continue;
            }

            var user = new FixTrackUser
            {
                FullName = account.FullName,
                Email = account.Email,
                UserName = account.Email,
                CreatedAtUtc = DateTime.UtcNow,
            };
            var created = await users.CreateAsync(user, account.Password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", created.Errors.Select(error => error.Description)));
            }

            await users.AddToRoleAsync(user, account.Role);
        }
    }
}

/// <summary>Cuenta creada solo en la base SQLite de las pruebas.</summary>
public sealed record TestAccount(string FullName, string Email, string Role, string Password);

/// <summary>
/// Cuentas de prueba. Las contraseñas se generan al azar en cada ejecución: no existe ninguna contraseña fija en el
/// repositorio y las cuentas desaparecen con la base en memoria.
/// </summary>
public static class TestAccounts
{
    public static TestAccount Administrator { get; } = new("Ana Administradora", "admin@fixtrack.test", Roles.Administrator, RandomPassword());

    public static TestAccount Operator { get; } = new("Oscar Operador", "operador@fixtrack.test", Roles.Operator, RandomPassword());

    public static IReadOnlyList<TestAccount> All { get; } = [Administrator, Operator];

    /// <summary>Cumple la política (12 caracteres o más, mayúsculas, minúsculas y números) sin ser predecible.</summary>
    public static string RandomPassword() => $"Pr{Guid.NewGuid():N}9z".Insert(10, "Q");
}
