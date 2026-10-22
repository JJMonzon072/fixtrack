using FixTrack.Demo.Scenario;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Demo;

/// <summary>
/// Herramienta de la demostración local (ver docs/demo-portafolio.md):
///   preparar  crea la base FixTrackDemo, el login de la aplicación y el certificado de Data Protection.
///   cargar    carga los datos ficticios en la base ya migrada (aplicar-migraciones de FixTrack).
/// La configuración llega por variables de entorno Demo__*. Nunca escribe contraseñas en la consola.
/// </summary>
internal static class DemoProgram
{
    private const string Usage = "Uso: FixTrack.Demo preparar | cargar";

    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var settings = DemoSettings.From(new ConfigurationBuilder().AddEnvironmentVariables().Build());
        try
        {
            if (settings.TargetProblems() is { Count: > 0 } problems)
            {
                throw new DemoException(string.Join(Environment.NewLine, problems));
            }

            switch (args)
            {
                case ["preparar"]:
                    await DemoDatabase.PrepareAsync(settings, cancellation.Token);
                    Console.WriteLine($"Base {settings.Database} lista; FixTrack se conecta con el login {settings.AppLogin} (dueño solo de esa base).");
                    var created = DemoCertificate.Ensure(
                        settings.CertificatePath, DemoSettings.Required(settings.CertificatePassword, nameof(DemoSettings.CertificatePassword)));
                    Console.WriteLine(created
                        ? $"Certificado de Data Protection creado en {settings.CertificatePath} (autofirmado, solo para esta demostración)."
                        : $"Certificado de Data Protection existente en {settings.CertificatePath}.");
                    return 0;

                case ["cargar"]:
                    await LoadAsync(settings, cancellation.Token);
                    return 0;

                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (DemoException error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static async Task LoadAsync(DemoSettings settings, CancellationToken cancellationToken)
    {
        var passwords = new DemoPasswords(
            DemoSettings.Required(settings.AdminPassword, nameof(DemoSettings.AdminPassword)),
            DemoSettings.Required(settings.OperatorPassword, nameof(DemoSettings.OperatorPassword)),
            DemoSettings.Required(settings.ReadOnlyPassword, nameof(DemoSettings.ReadOnlyPassword)));
        var connectionString = settings.AppConnectionString();
        await (await DemoDatabase.OpenAsync(connectionString, cancellationToken)).DisposeAsync();

        await using var services = DemoServices.Create(connectionString);
        await using (var scope = services.CreateAsyncScope())
        {
            var pending = await scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().Database.GetPendingMigrationsAsync(cancellationToken);
            if (pending.Any())
            {
                throw new DemoException($"La base {settings.Database} no tiene el esquema al día: aplica antes las migraciones (comando aplicar-migraciones de FixTrack).");
            }
        }

        var scenario = DemoScenario.Create(DateTime.UtcNow, DemoServices.WorkshopTimeZone);
        var outcome = await new DemoLoader(services).LoadAsync(scenario, passwords, cancellationToken);
        Console.WriteLine(outcome == DemoLoadOutcome.AlreadyLoaded
            ? "La base ya contiene la demostración completa: no se cargó nada nuevo."
            : "Datos ficticios cargados con los servicios de FixTrack.");

        await using var summaryScope = services.CreateAsyncScope();
        var db = summaryScope.ServiceProvider.GetRequiredService<FixTrackDbContext>();
        var statuses = await db.ServiceOrders.GroupBy(order => order.Status).Select(group => new { group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
        Console.WriteLine(
            $"{await db.Technicians.CountAsync(cancellationToken)} técnicos, {await db.Customers.CountAsync(cancellationToken)} clientes, " +
            $"{await db.Devices.CountAsync(cancellationToken)} equipos y {statuses.Sum(status => status.Count)} órdenes: " +
            string.Join(", ", statuses.OrderBy(status => status.Key).Select(status => $"{ServiceOrderWorkflow.Name(status.Key).ToLowerInvariant()} {status.Count}")) + ".");
        foreach (var account in new[] { scenario.Administrator, scenario.Operator, scenario.ReadOnly })
        {
            Console.WriteLine($"Cuenta {account.Role}: {account.Email}");
        }
    }
}
