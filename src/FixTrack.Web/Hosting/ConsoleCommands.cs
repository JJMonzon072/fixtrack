using System.Text;
using FixTrack.Application.Security;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Web.Hosting;

/// <summary>
/// Comandos explícitos de administración que se ejecutan en lugar de iniciar el servidor web:
/// <list type="bullet">
/// <item><c>crear-administrador</c>: crea el primer administrador con los datos de la sección <c>InitialAdmin</c>
/// (secretos de usuario o variables de entorno). Si falta la contraseña y hay una terminal interactiva, la pide sin
/// mostrarla. No hace nada si ya existe un administrador habilitado.</item>
/// <item><c>aplicar-migraciones</c>: aplica a la base configurada las migraciones pendientes.</item>
/// <item><c>proteger-claves</c>: crea una clave de Data Protection cifrada con el certificado configurado; con
/// <c>--revocar-sin-cifrar</c>, además revoca las claves guardadas sin cifrar.</item>
/// </list>
/// Nunca escriben contraseñas en la consola ni en las bitácoras.
/// </summary>
public static class ConsoleCommands
{
    public const string CreateAdministrator = "crear-administrador";
    public const string ApplyMigrations = "aplicar-migraciones";
    public const string ProtectKeys = "proteger-claves";
    public const string RevokeUnencryptedOption = "--revocar-sin-cifrar";

    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] is CreateAdministrator or ApplyMigrations or ProtectKeys;

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return args[0] switch
        {
            CreateAdministrator => await CreateAdministratorAsync(scope.ServiceProvider, app.Configuration),
            ProtectKeys => await ProtectKeysAsync(scope.ServiceProvider, args.Contains(RevokeUnencryptedOption)),
            _ => await ApplyMigrationsAsync(scope.ServiceProvider),
        };
    }

    private static async Task<int> CreateAdministratorAsync(IServiceProvider services, IConfiguration configuration)
    {
        var email = configuration["InitialAdmin:Email"];
        var fullName = configuration["InitialAdmin:FullName"];
        var password = configuration["InitialAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName))
        {
            Console.Error.WriteLine("Faltan InitialAdmin:Email e InitialAdmin:FullName (secretos de usuario o variables InitialAdmin__Email e InitialAdmin__FullName).");
            return 2;
        }

        if (string.IsNullOrEmpty(password))
        {
            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine("Falta InitialAdmin:Password y no hay una terminal interactiva para pedirla.");
                return 2;
            }

            password = ReadHidden("Contraseña del administrador: ");
            if (password != ReadHidden("Repite la contraseña: "))
            {
                Console.Error.WriteLine("Las contraseñas no coinciden. No se creó la cuenta.");
                return 1;
            }
        }

        var result = await services.GetRequiredService<IUserAccountService>()
            .CreateInitialAdministratorAsync(email, fullName, password, CancellationToken.None);
        switch (result.Outcome)
        {
            case InitialAdministratorOutcome.Created:
                Console.WriteLine($"Administrador creado: {email.Trim().ToLowerInvariant()}.");
                return 0;
            case InitialAdministratorOutcome.AlreadyExists:
                Console.WriteLine("Ya existe un administrador habilitado. No se creó otra cuenta: usa la administración de usuarios.");
                return 3;
            default:
                Console.Error.WriteLine("No se creó la cuenta:");
                foreach (var error in result.Errors)
                {
                    Console.Error.WriteLine($" - {error}");
                }

                return 1;
        }
    }

    private static async Task<int> ApplyMigrationsAsync(IServiceProvider services)
    {
        var database = services.GetRequiredService<FixTrackDbContext>().Database;
        var pending = (await database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            Console.WriteLine("La base de datos ya está al día: no hay migraciones pendientes.");
            return 0;
        }

        Console.WriteLine($"Aplicando {pending.Count} migración(es): {string.Join(", ", pending)}");
        await database.MigrateAsync();
        Console.WriteLine("Migraciones aplicadas.");
        return 0;
    }

    private static async Task<int> ProtectKeysAsync(IServiceProvider services, bool revokeUnencrypted)
    {
        var result = await services.GetRequiredService<DataProtectionKeyRotation>().ProtectAsync(revokeUnencrypted, CancellationToken.None);
        if (!result.Configured)
        {
            Console.Error.WriteLine(result.Problem);
            return 2;
        }

        Console.WriteLine(result.CreatedKey is { } created
            ? $"Se creó la clave cifrada {created:D}."
            : "Ya hay una clave cifrada activa: no se creó otra.");
        if (result.RevokedKeys.Count > 0)
        {
            Console.WriteLine($"Claves sin cifrar revocadas: {result.RevokedKeys.Count}. Las sesiones emitidas con ellas deben iniciarse de nuevo.");
        }

        Console.WriteLine("Claves del anillo (sin su material):");
        foreach (var key in result.Keys)
        {
            Console.WriteLine(
                $" - {key.Id:D} · creada {key.CreatedAt:yyyy-MM-dd HH:mm} UTC · activa desde {key.ActivatesAt:yyyy-MM-dd HH:mm} · " +
                $"expira {key.ExpiresAt:yyyy-MM-dd} · {(key.IsEncrypted ? "cifrada" : "SIN CIFRAR")}{(key.IsRevoked ? " · revocada" : string.Empty)}");
        }

        if (!revokeUnencrypted && result.Keys.Any(key => !key.IsEncrypted && !key.IsRevoked))
        {
            Console.WriteLine(
                "Quedan claves sin cifrar, que se siguen usando: la clave cifrada pasa a ser la predeterminada cuando termina su " +
                $"periodo de propagación (48 horas). Después, ejecuta de nuevo con {RevokeUnencryptedOption}. Si no hay sesiones " +
                "que conservar (por ejemplo, antes del primer despliegue), puedes hacerlo ya: la clave cifrada se usará de inmediato.");
        }

        return 0;
    }

    /// <summary>Lee una línea sin mostrar lo que se escribe.</summary>
    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return text.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length > 0)
                {
                    text.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }
    }
}
