using System.Text;
using FixTrack.Application.Security;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Infrastructure.Persistence.Backups;
using FixTrack.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Web.Hosting;

/// <summary>
/// Comandos explícitos de administración que se ejecutan en lugar de iniciar el servidor web:
/// <list type="bullet">
/// <item><c>crear-administrador</c>: crea el primer administrador con los datos de la sección <c>InitialAdmin</c>
/// (secretos de usuario o variables de entorno). Si falta la contraseña y hay una terminal interactiva, la pide sin
/// mostrarla. No hace nada si ya existe un administrador habilitado.</item>
/// <item><c>crear-cuenta-consulta &lt;correo&gt; "&lt;nombre completo&gt;"</c>: crea una cuenta de solo consulta (ve todo,
/// no cambia nada) para demostraciones o para preparar el manual. Pide la contraseña dos veces sin mostrarla; sin una
/// terminal interactiva la toma de <c>ReadOnlyAccount:Password</c>.</item>
/// <item><c>aplicar-migraciones</c>: aplica a la base configurada las migraciones pendientes.</item>
/// <item><c>proteger-claves</c>: crea una clave de Data Protection cifrada con el certificado configurado; con
/// <c>--revocar-sin-cifrar</c>, además revoca las claves guardadas sin cifrar; con <c>--revocar-ilegibles</c>, las que el
/// certificado configurado no descifra (si el anterior se perdió o se filtró).</item>
/// <item><c>exportar-respaldo &lt;carpeta&gt;</c>: respaldo lógico de la base (solo lectura) en una carpeta fuera del
/// repositorio. <c>verificar-respaldo &lt;archivo&gt;</c> comprueba un respaldo sin conectarse a la base, y
/// <c>restaurar-respaldo &lt;archivo&gt;</c> lo carga en una base vacía (ver <see cref="DatabaseBackup"/>).</item>
/// </list>
/// Nunca escriben contraseñas en la consola ni en las bitácoras.
/// </summary>
public static class ConsoleCommands
{
    public const string CreateAdministrator = "crear-administrador";
    public const string CreateReadOnlyAccount = "crear-cuenta-consulta";
    public const string ApplyMigrations = "aplicar-migraciones";
    public const string ProtectKeys = "proteger-claves";
    public const string RevokeUnencryptedOption = "--revocar-sin-cifrar";
    public const string ExportBackup = "exportar-respaldo";
    public const string VerifyBackup = "verificar-respaldo";
    public const string RestoreBackup = "restaurar-respaldo";

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && args[0] is CreateAdministrator or CreateReadOnlyAccount or ApplyMigrations or ProtectKeys or ExportBackup or VerifyBackup or RestoreBackup;

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return args[0] switch
        {
            CreateAdministrator => await CreateAdministratorAsync(scope.ServiceProvider, app.Configuration),
            CreateReadOnlyAccount => await CreateReadOnlyAccountAsync(scope.ServiceProvider, app.Configuration, args),
            ProtectKeys => await ProtectKeysAsync(
                scope.ServiceProvider,
                args.Contains(RevokeUnencryptedOption),
                args.Contains(DataProtectionKeyCheck.RevokeUnreadableOption)),
            ExportBackup or VerifyBackup or RestoreBackup => await BackupAsync(scope.ServiceProvider, args),
            _ => await ApplyMigrationsAsync(scope.ServiceProvider),
        };
    }

    private static async Task<int> BackupAsync(IServiceProvider services, string[] args)
    {
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine(args[0] == ExportBackup
                ? $"Uso: {ExportBackup} <carpeta fuera del repositorio>"
                : $"Uso: {args[0]} <archivo .zip del respaldo>");
            return 2;
        }

        try
        {
            switch (args[0])
            {
                case ExportBackup:
                    var export = await services.GetRequiredService<DatabaseBackup>().ExportAsync(args[1], CancellationToken.None);
                    WriteTables(export.Manifest);
                    Console.WriteLine($"Respaldo creado: {export.FilePath}");
                    Console.WriteLine($"SHA-256 del archivo: {export.FileSha256}");
                    Console.WriteLine("Contiene datos personales, hashes de contraseñas y claves de Data Protection: guárdalo cifrado y fuera de GitHub.");
                    return 0;
                case VerifyBackup:
                    var manifest = await DatabaseBackup.VerifyAsync(args[1], CancellationToken.None);
                    WriteTables(manifest);
                    Console.WriteLine("El respaldo está íntegro: cada tabla coincide con su manifiesto.");
                    return 0;
                default:
                    var restore = await services.GetRequiredService<DatabaseBackup>().RestoreAsync(args[1], CancellationToken.None);
                    WriteTables(restore.Manifest);
                    if (restore.MigratedEmptyDatabase)
                    {
                        Console.WriteLine($"La base estaba vacía: se le aplicaron las {restore.Manifest.Migrations.Count} migraciones del respaldo.");
                    }

                    if (restore.AlreadyPresent.Count > 0)
                    {
                        Console.WriteLine($"Ya estaban idénticas (datos iniciales de las migraciones): {string.Join(", ", restore.AlreadyPresent)}.");
                    }

                    Console.WriteLine("Restauración completa: cada tabla de la base coincide con el hash de su manifiesto.");
                    if (restore.PendingMigrations.Count > 0)
                    {
                        Console.WriteLine($"El respaldo es anterior a esta versión: ejecuta {ApplyMigrations} para aplicar {restore.PendingMigrations.Count} migración(es) más.");
                    }

                    return 0;
            }
        }
        catch (BackupException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void WriteTables(BackupManifest manifest)
    {
        Console.WriteLine(
            $"Base {manifest.Database} · {manifest.CreatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC · lectura {manifest.Isolation} · " +
            $"{manifest.Migrations.Count} migraciones (última: {manifest.Migrations.LastOrDefault() ?? "ninguna"})");
        foreach (var table in manifest.Tables)
        {
            Console.WriteLine($" - {table.Name}: {table.Rows} fila(s) · sha256 {table.Sha256[..16]}…");
        }

        Console.WriteLine($"Total: {manifest.Tables.Count} tablas, {manifest.TotalRows} filas.");
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

    private static async Task<int> CreateReadOnlyAccountAsync(IServiceProvider services, IConfiguration configuration, string[] args)
    {
        if (args.Length != 3 || string.IsNullOrWhiteSpace(args[1]) || string.IsNullOrWhiteSpace(args[2]))
        {
            Console.Error.WriteLine($"Uso: {CreateReadOnlyAccount} <correo> \"<nombre completo>\"");
            return 2;
        }

        var password = configuration["ReadOnlyAccount:Password"];
        var confirmPassword = password;
        if (string.IsNullOrEmpty(password))
        {
            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine("Falta ReadOnlyAccount:Password y no hay una terminal interactiva para pedirla.");
                return 2;
            }

            password = ReadHidden("Contraseña de la cuenta de consulta: ");
            confirmPassword = ReadHidden("Repite la contraseña: ");
        }

        var result = await services.GetRequiredService<IUserAccountService>()
            .CreateReadOnlyAccountAsync(args[1], args[2], password, confirmPassword, CancellationToken.None);
        if (result.Succeeded)
        {
            Console.WriteLine($"Cuenta de solo consulta creada: {args[1].Trim().ToLowerInvariant()}. Puede ver todas las pantallas, pero no guardar cambios.");
            return 0;
        }

        Console.Error.WriteLine("No se creó la cuenta:");
        foreach (var error in result.Message is { } message ? [message] : result.Errors.Values)
        {
            Console.Error.WriteLine($" - {error}");
        }

        return 1;
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

    private static async Task<int> ProtectKeysAsync(IServiceProvider services, bool revokeUnencrypted, bool revokeUnreadable)
    {
        var result = await services.GetRequiredService<DataProtectionKeyRotation>()
            .ProtectAsync(revokeUnencrypted, CancellationToken.None, revokeUnreadable);
        if (!result.Configured)
        {
            Console.Error.WriteLine(result.Problem);
            return 2;
        }

        if (result.Problem is not null)
        {
            Console.Error.WriteLine(result.Problem);
            WriteKeys(result.Keys);
            return 1;
        }

        Console.WriteLine(result.CreatedKey is { } created
            ? $"Se creó la clave cifrada {created:D}."
            : "Ya hay una clave cifrada activa: no se creó otra.");
        if (result.RevokedKeys.Count > 0)
        {
            Console.WriteLine(
                $"Claves revocadas: {result.RevokedKeys.Count}. Las sesiones emitidas con ellas deben iniciarse de nuevo. " +
                "Reinicia la aplicación en ejecución: conserva el anillo de claves en memoria.");
        }

        WriteKeys(result.Keys);

        if (!revokeUnencrypted && result.Keys.Any(key => !key.IsEncrypted && !key.IsRevoked))
        {
            Console.WriteLine(
                "Quedan claves sin cifrar, que se siguen usando: la clave cifrada pasa a ser la predeterminada cuando termina su " +
                $"periodo de propagación (48 horas). Después, ejecuta de nuevo con {RevokeUnencryptedOption}. Si no hay sesiones " +
                "que conservar (por ejemplo, antes del primer despliegue), puedes hacerlo ya: la clave cifrada se usará de inmediato.");
        }

        return 0;
    }

    private static void WriteKeys(IEnumerable<DataProtectionKeyRotation.KeyStatus> keys)
    {
        Console.WriteLine("Claves del anillo (sin su material):");
        foreach (var key in keys)
        {
            Console.WriteLine(
                $" - {key.Id:D} · creada {key.CreatedAt:yyyy-MM-dd HH:mm} UTC · activa desde {key.ActivatesAt:yyyy-MM-dd HH:mm} · " +
                $"expira {key.ExpiresAt:yyyy-MM-dd} · {(key.IsEncrypted ? "cifrada" : "SIN CIFRAR")}" +
                $"{(key.IsReadable ? string.Empty : " · ILEGIBLE con este certificado")}{(key.IsRevoked ? " · revocada" : string.Empty)}");
        }
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
