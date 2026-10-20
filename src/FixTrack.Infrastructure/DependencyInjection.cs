using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FixTrack.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra los servicios de infraestructura. La persistencia con SQL Server
    /// se incorporará aquí en una fase posterior.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Reloj del sistema. Las demás capas dependen de TimeProvider y no de DateTime.Now,
        // para poder sustituirlo por un reloj fijo en las pruebas.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
