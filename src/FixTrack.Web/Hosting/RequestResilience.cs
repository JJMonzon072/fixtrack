using Microsoft.AspNetCore.Http.Timeouts;

namespace FixTrack.Web.Hosting;

/// <summary>
/// Límites para que una base que no responde no deje solicitudes colgadas: cada solicitud se corta a los
/// <see cref="Timeout"/> con 503, y los fallos temporales de la base se muestran como "servicio no disponible".
/// </summary>
public static class RequestResilience
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddRequestResilience(this IServiceCollection services)
    {
        services.AddExceptionHandler<DatabaseUnavailableHandler>();
        services.AddRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy
        {
            Timeout = Timeout,
            TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable,
        });
        return services;
    }
}
