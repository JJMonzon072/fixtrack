using System.Threading.RateLimiting;
using FixTrack.Infrastructure.Identity;
using FixTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace FixTrack.Web.Security;

public static class SecurityServiceCollectionExtensions
{
    /// <summary>Nombre de la política de límite de intentos del formulario de inicio de sesión.</summary>
    public const string LoginRateLimit = "inicio-de-sesion";

    public const string LoginPath = "/cuenta/iniciar-sesion";

    /// <summary>
    /// Autenticación con ASP.NET Core Identity y cookie, políticas de autorización, claves de Data Protection en la base
    /// y límite de intentos de inicio de sesión por dirección IP.
    /// </summary>
    public static IServiceCollection AddSecurity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services
            .AddIdentity<FixTrackUser, IdentityRole>(IdentitySettings.Configure)
            .AddEntityFrameworkStores<FixTrackDbContext>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<SpanishIdentityErrorDescriber>()
            .AddClaimsPrincipalFactory<FixTrackClaimsPrincipalFactory>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "FixTrack.Sesion";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;

            // Fuera de Desarrollo la cookie solo viaja por HTTPS (también detrás del proxy de Render).
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

            // Sesión de navegador (no persistente) que expira tras 2 horas sin actividad.
            options.ExpireTimeSpan = TimeSpan.FromHours(2);
            options.SlidingExpiration = true;

            options.LoginPath = LoginPath;
            options.LogoutPath = "/cuenta/cerrar-sesion";
            options.AccessDeniedPath = "/cuenta/acceso-denegado";
            options.ReturnUrlParameter = "volver";
            options.Events.OnRedirectToLogin = context => RedirectOrStatus(context, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context =>
            {
                // Sin redirección: la página de error muestra "No tienes permiso" con el código 403.
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // Cambios de rol, contraseña o estado de una cuenta cierran sus sesiones en, como máximo, un minuto.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddAuthorization(AuthorizationPolicies.Configure);
        services.AddScoped<FailedLoginEqualizer>();

        // Claves que cifran cookies y tokens: en la base (cifradas con un certificado) fuera de Desarrollo.
        services.AddFixTrackDataProtection(configuration, environment);

        var attemptsPerMinute = configuration.GetValue("Security:LoginAttemptsPerMinute", 10);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = attemptsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        return services;
    }

    /// <summary>Las solicitudes de datos (JSON) reciben 401 en lugar de la redirección al inicio de sesión.</summary>
    private static Task RedirectOrStatus(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        var accept = context.Request.Headers.Accept.ToString();
        if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}
