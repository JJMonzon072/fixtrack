using System.Text.RegularExpressions;
using FixTrack.Application.Security;
using FixTrack.Infrastructure.Identity;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Tests.Infrastructure;
using FixTrack.Web.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
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
/// Crea las cuentas de <see cref="TestAccounts"/>, con contraseñas aleatorias generadas en cada ejecución.
/// </summary>
public sealed partial class FixTrackWebApplicationFactory : WebApplicationFactory<Program>
{
    // El esquema se crea antes de iniciar la aplicación: Data Protection lee sus claves de la base al arrancar.
    private readonly SqliteTestDatabase _database = new();

    // Las claves de Data Protection van en otra base en memoria, con una conexión por contexto. EF Core registra sus
    // funciones en la conexión cada vez que crea un contexto; con la conexión compartida de _database, la actualización
    // del anillo de claves en segundo plano (.NET 9+) podía hacerlo mientras otra consulta leía en esa misma conexión
    // (SQLite Error 5: "unable to delete/modify user-function due to active statements").
    private readonly KeysDatabase _keys = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Valor ficticio: solo evita que la aplicación se detenga por falta de configuración.
        builder.UseSetting("ConnectionStrings:FixTrack", "Server=pruebas-sin-base-real;Database=NoUsar;");

        // Muchas pruebas inician sesión desde la misma "dirección": el límite real se prueba aparte.
        builder.UseSetting("Security:LoginAttemptsPerMinute", "10000");

        // El tiempo mínimo de un inicio de sesión fallido se prueba aparte; aquí solo haría lentas las pruebas.
        builder.UseSetting(FailedLoginEqualizer.MinimumDurationSetting, "0");

        // Las claves de Data Protection van a la base de la prueba, cifradas con un certificado generado al azar.
        builder.UseProtectedKeysInDatabase();

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<FixTrackDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<FixTrackDbContext>>();
            services.RemoveAll<FixTrackDbContext>();
            services.AddDbContext<FixTrackDbContext>(options => options.UseSqlite(_database.Connection));
            services.RemoveAll<DbContextOptions<DataProtectionKeysDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<DataProtectionKeysDbContext>>();
            services.RemoveAll<DataProtectionKeysDbContext>();
            services.AddDbContext<DataProtectionKeysDbContext>(options => options.UseSqlite(_keys.ConnectionString));
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
            _keys.Dispose();
        }
    }

    /// <summary>
    /// Base en memoria compartida entre conexiones (<c>Cache=Shared</c>): existe mientras la conexión de mantenimiento
    /// siga abierta, y cada contexto abre la suya. SQLite serializa los accesos con bloqueos y Microsoft.Data.Sqlite
    /// reintenta mientras están ocupados.
    /// </summary>
    private sealed partial class KeysDatabase : IDisposable
    {
        private readonly SqliteConnection _keepAlive;

        public KeysDatabase()
        {
            ConnectionString = $"Data Source=fixtrack-claves-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            _keepAlive = new SqliteConnection(ConnectionString);
            _keepAlive.Open();
            // El contexto de las claves excluye su tabla de las migraciones (la crea FixTrackDbContext): se usa la misma
            // instrucción que genera el modelo principal.
            using var model = new FixTrackDbContext(new DbContextOptionsBuilder<FixTrackDbContext>().UseSqlite(_keepAlive).Options);
            using var create = _keepAlive.CreateCommand();
            create.CommandText = CreateKeysTable().Match(model.Database.GenerateCreateScript()).Value;
            create.ExecuteNonQuery();
        }

        public string ConnectionString { get; }

        public void Dispose() => _keepAlive.Dispose();

        [GeneratedRegex(@"CREATE TABLE ""DataProtectionKeys"" \(.*?\);", RegexOptions.Singleline)]
        private static partial Regex CreateKeysTable();
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

    public static TestAccount ReadOnly { get; } = new("Carla Consulta", "consulta@fixtrack.test", Roles.ReadOnly, RandomPassword());

    public static IReadOnlyList<TestAccount> All { get; } = [Administrator, Operator, ReadOnly];

    /// <summary>Cumple la política (12 caracteres o más, mayúsculas, minúsculas y números) sin ser predecible.</summary>
    public static string RandomPassword() => $"Pr{Guid.NewGuid():N}9z".Insert(10, "Q");
}
