using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace FixTrack.Web.Hosting;

/// <summary>
/// Puntos de comprobación públicos y sin datos sensibles: solo responden "Healthy" o "Unhealthy".
/// <list type="bullet">
/// <item><c>/salud</c>: la aplicación está en ejecución (para el health check de Render).</item>
/// <item><c>/salud/bd</c>: además, la base de datos responde.</item>
/// </list>
/// </summary>
public static class HealthEndpoints
{
    public const string LivenessPath = "/salud";
    public const string DatabasePath = "/salud/bd";
    private const string DatabaseTag = "bd";

    public static IServiceCollection AddHealthEndpoints(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("base-de-datos", tags: [DatabaseTag]);
        return services;
    }

    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks(DatabasePath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(DatabaseTag) }).AllowAnonymous();
        return app;
    }

    public static bool IsHealthRequest(HttpContext context) => context.Request.Path.StartsWithSegments(LivenessPath);
}
