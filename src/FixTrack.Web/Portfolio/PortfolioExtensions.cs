using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using FixTrack.Web.Api;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FixTrack.Web.Portfolio;

public static class PortfolioExtensions
{
    /// <summary>Política de límite de solicitudes de la API de consulta.</summary>
    public const string ApiRateLimit = "api-v1";

    /// <summary>Cookie de sesión de FixTrack (ver SecurityServiceCollectionExtensions).</summary>
    private const string SessionCookieName = "FixTrack.Sesion";

    /// <summary>
    /// Registra los extras de portafolio. Sus servicios existen siempre (no cambian nada mientras nadie los use); sus
    /// controladores solo se registran si el extra está habilitado en <c>PortfolioFeatures</c>.
    /// </summary>
    public static IServiceCollection AddPortfolioExtras(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PortfolioFeatures>()
            .Bind(configuration.GetSection(PortfolioFeatures.SectionName))
            .Validate(features => features.ExportMaxRows is > 0 and <= 50_000, "PortfolioFeatures:ExportMaxRows debe estar entre 1 y 50000.")
            .Validate(features => features.ApiRequestsPerMinute is > 0 and <= 10_000, "PortfolioFeatures:ApiRequestsPerMinute debe estar entre 1 y 10000.")
            .ValidateOnStart();
        services.AddTransient<IConfigureOptions<MvcOptions>, PortfolioMvcSetup>();

        // API: límite de solicitudes por sesión (o por dirección IP sin sesión). Solo se aplica a sus controladores. El
        // límite se evalúa antes de la autenticación (así está ordenado el proceso de FixTrack): se usa la huella de la
        // cookie de sesión, no su contenido. Sin una sesión válida la solicitud termina en 401 de todos modos.
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<PortfolioFeatures>>((options, features) =>
            options.AddPolicy(ApiRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Request.Cookies[SessionCookieName] is { Length: > 0 } session
                    ? $"sesion:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session)))[..32]}"
                    : $"ip:{context.Connection.RemoteIpAddress}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = features.Value.ApiRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                })));

        // API: sin sesión o sin permiso, Problem Details 401/403 en JSON en lugar de la redirección o la página de error.
        // Solo para las rutas /api con el extra habilitado; el resto conserva el comportamiento de siempre.
        services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
        {
            var toLogin = options.Events.OnRedirectToLogin;
            var toAccessDenied = options.Events.OnRedirectToAccessDenied;
            options.Events.OnRedirectToLogin = context =>
                IsApiWithExtra(context.HttpContext) ? ApiProblem.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized) : toLogin(context);
            options.Events.OnRedirectToAccessDenied = context =>
                IsApiWithExtra(context.HttpContext) ? ApiProblem.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden) : toAccessDenied(context);
        });

        return services;
    }

    /// <summary>
    /// Deja constancia en la bitácora de los extras habilitados y, con la API habilitada, responde con Problem Details
    /// los errores de sus rutas (404, 405, 429…). Con todos desactivados no agrega nada al proceso de las solicitudes.
    /// </summary>
    public static WebApplication UsePortfolioExtras(this WebApplication app)
    {
        var features = app.Services.GetRequiredService<IOptions<PortfolioFeatures>>().Value;
        if (features.EnabledNames() is { Count: > 0 } enabled)
        {
            app.Logger.LogInformation("Extras de portafolio habilitados: {Features}.", string.Join(", ", enabled));
        }

        if (features.ReadOnlyApi)
        {
            app.UseWhen(
                ApiProblem.IsApiRequest,
                api => api.UseStatusCodePages(context => ApiProblem.WriteAsync(context.HttpContext, context.HttpContext.Response.StatusCode)));
        }

        return app;
    }

    private static bool IsApiWithExtra(HttpContext context) =>
        ApiProblem.IsApiRequest(context) && context.RequestServices.GetRequiredService<IOptions<PortfolioFeatures>>().Value.ReadOnlyApi;
}
