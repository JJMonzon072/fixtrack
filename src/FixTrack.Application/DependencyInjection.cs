using FixTrack.Application.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Application;

public static class DependencyInjection
{
    /// <summary>Registra los servicios de la capa de aplicación.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IModuleCatalog, ModuleCatalog>();

        return services;
    }
}
