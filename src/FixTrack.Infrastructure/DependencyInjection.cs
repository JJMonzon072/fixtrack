using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Dashboard;
using FixTrack.Application.Devices;
using FixTrack.Application.Security;
using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using FixTrack.Infrastructure.Identity;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Infrastructure.Persistence.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FixTrack.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Nombre de la cadena de conexión: <c>ConnectionStrings:FixTrack</c>.</summary>
    public const string ConnectionStringName = "FixTrack";

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

        services.AddDbContext<FixTrackDbContext>(options => options.UseSqlServer(
            connectionString,
            sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));

        // Claves de Data Protection: falla rápido si la base no responde (ver DataProtectionKeysDbContext).
        var keysConnection = new SqlConnectionStringBuilder(connectionString);
        keysConnection.ConnectTimeout = Math.Min(keysConnection.ConnectTimeout, DataProtectionKeysTimeoutSeconds);
        services.AddDbContext<DataProtectionKeysDbContext>(options => options.UseSqlServer(
            keysConnection.ConnectionString,
            sql => sql
                .CommandTimeout(DataProtectionKeysTimeoutSeconds)
                .EnableRetryOnFailure(maxRetryCount: 1, maxRetryDelay: TimeSpan.FromSeconds(1), errorNumbersToAdd: null)));

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAuditLog, EfAuditLog>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<ITechnicianRepository, TechnicianRepository>();
        services.AddScoped<IServiceOrderRepository, ServiceOrderRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();

        // Requiere el UserManager de ASP.NET Core Identity, que registra la capa de presentación (AddIdentity).
        services.AddScoped<IUserAccountService, UserAccountService>();

        return services;
    }
}
