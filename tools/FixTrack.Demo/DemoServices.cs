using FixTrack.Application;
using FixTrack.Application.Common;
using FixTrack.Infrastructure;
using FixTrack.Infrastructure.Identity;
using FixTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Demo;

/// <summary>
/// Servicios de FixTrack para la carga: las mismas capas de aplicación e infraestructura que la aplicación web, con el
/// reloj y la sesión de la demostración en lugar del reloj del sistema y de la sesión HTTP.
/// </summary>
internal static class DemoServices
{
    /// <summary>Zona horaria del taller ficticio (la misma que usa la imagen de FixTrack).</summary>
    public static TimeZoneInfo WorkshopTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/Guatemala");

    /// <param name="replaceDatabase">Las pruebas cambian SQL Server por SQLite; la herramienta no lo usa.</param>
    public static ServiceProvider Create(string connectionString, Action<IServiceCollection>? replaceDatabase = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{Infrastructure.DependencyInjection.ConnectionStringName}"] = connectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Antes de AddInfrastructure, que solo registra el reloj del sistema si no hay otro.
        services.AddSingleton(new DemoClock(WorkshopTimeZone));
        services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<DemoClock>());
        services.AddSingleton<DemoSession>();
        services.AddSingleton<ICurrentUser>(provider => provider.GetRequiredService<DemoSession>());

        services.AddApplication().AddInfrastructure(configuration);

        // Las cuentas se crean con el servicio de cuentas de FixTrack: la misma política de contraseñas y los mismos roles.
        services.AddIdentityCore<FixTrackUser>(IdentitySettings.Configure)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<FixTrackDbContext>()
            .AddErrorDescriber<SpanishIdentityErrorDescriber>();

        replaceDatabase?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
