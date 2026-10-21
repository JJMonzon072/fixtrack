using FixTrack.Application.Auditing;
using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Dashboard;
using FixTrack.Application.Devices;
using FixTrack.Application.Reports;
using FixTrack.Application.Security;
using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using FixTrack.Infrastructure.Identity;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Infrastructure.Persistence.Backups;
using FixTrack.Infrastructure.Persistence.Repositories;
using FixTrack.Infrastructure.Persistence.Resilience;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FixTrack.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Nombre de la cadena de conexión: <c>ConnectionStrings:FixTrack</c>.</summary>
    public const string ConnectionStringName = "FixTrack";

    /// <summary>Tiempo máximo para abrir una conexión (la cadena puede indicar uno menor).</summary>
    public const int ConnectTimeoutSeconds = 15;

    /// <summary>Tiempo máximo de una consulta.</summary>
    public const int CommandTimeoutSeconds = 15;

    /// <summary>Tiempo máximo de conexión y de consulta del anillo de claves de Data Protection.</summary>
    public const int DataProtectionKeysTimeoutSeconds = 5;

    /// <summary>Registra los servicios de infraestructura: reloj del sistema y persistencia con SQL Server.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Reloj del sistema. Las demás capas dependen de TimeProvider y no de DateTime.Now,
        // para poder sustituirlo por un reloj fijo en las pruebas.
        services.TryAddSingleton(TimeProvider.System);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Falta la cadena de conexión 'ConnectionStrings:{ConnectionStringName}'. " +
                "En desarrollo se configura con dotnet user-secrets; en un servidor, con la variable de entorno " +
                $"ConnectionStrings__{ConnectionStringName}. Consulta el README.");
        }

        // Fallos temporales de Somee: tiempos acotados, pocos reintentos y un disyuntor que evita insistir contra una base
        // caída (ver DatabaseAvailability). Una solicitud además se corta a los 30 s (UseRequestTimeouts).
        services.AddSingleton<DatabaseAvailability>();
        services.AddSingleton<DatabaseCircuitInterceptor>();
        services.AddSingleton<DatabaseCommandMonitor>();
        var mainConnection = new SqlConnectionStringBuilder(connectionString);
        mainConnection.ConnectTimeout = Math.Min(mainConnection.ConnectTimeout, ConnectTimeoutSeconds);
        services.AddDbContext<FixTrackDbContext>((provider, options) => options
            .UseSqlServer(
                mainConnection.ConnectionString,
                sql => sql
                    .CommandTimeout(CommandTimeoutSeconds)
                    .EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
            .AddInterceptors(provider.GetRequiredService<DatabaseCircuitInterceptor>(), provider.GetRequiredService<DatabaseCommandMonitor>())
            .ConfigureWarnings(QuietExpectedFailures));

        // Claves de Data Protection: falla rápido si la base no responde (ver DataProtectionKeysDbContext).
        var keysConnection = new SqlConnectionStringBuilder(connectionString);
        keysConnection.ConnectTimeout = Math.Min(keysConnection.ConnectTimeout, DataProtectionKeysTimeoutSeconds);
        services.AddDbContext<DataProtectionKeysDbContext>((provider, options) => options
            .UseSqlServer(
                keysConnection.ConnectionString,
                sql => sql
                    .CommandTimeout(DataProtectionKeysTimeoutSeconds)
                    .EnableRetryOnFailure(maxRetryCount: 1, maxRetryDelay: TimeSpan.FromSeconds(1), errorNumbersToAdd: null))
            .AddInterceptors(provider.GetRequiredService<DatabaseCircuitInterceptor>(), provider.GetRequiredService<DatabaseCommandMonitor>())
            .ConfigureWarnings(QuietExpectedFailures));

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAuditLog, EfAuditLog>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<ITechnicianRepository, TechnicianRepository>();
        services.AddScoped<IServiceOrderRepository, ServiceOrderRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<DatabaseBackup>();

        // Requiere el UserManager de ASP.NET Core Identity, que registra la capa de presentación (AddIdentity).
        services.AddScoped<IUserAccountService, UserAccountService>();

        return services;
    }

    /// <summary>
    /// EF Core registra como error, con la traza completa, cada conexión fallida, cada consulta que falla y cada
    /// SaveChanges que falla. La misma excepción llega después a quien la maneja: el manejador de 503 (una línea por
    /// solicitud), el código que la convierte en un resultado (por ejemplo, la orden duplicada) o el middleware de
    /// errores, que registra las no manejadas. Se bajan a Debug para que una caída de la base no repita cuatro trazas
    /// por solicitud. Los errores de comando (Database.Command) se conservan: incluyen el SQL que falló.
    /// </summary>
    private static void QuietExpectedFailures(WarningsConfigurationBuilder warnings) => warnings.Log(
        (RelationalEventId.ConnectionError, LogLevel.Debug),
        (CoreEventId.QueryIterationFailed, LogLevel.Debug),
        (CoreEventId.SaveChangesFailed, LogLevel.Debug));

}
