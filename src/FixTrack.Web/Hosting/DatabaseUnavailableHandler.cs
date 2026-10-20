using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using FixTrack.Application.Common;
using FixTrack.Infrastructure.Persistence.Resilience;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace FixTrack.Web.Hosting;

/// <summary>
/// Convierte un fallo temporal de la base de datos en una respuesta 503 con <c>Retry-After</c> y una página breve en
/// español, sin detalles técnicos. La página es estática: no usa la plantilla ni la sesión, que también necesitarían la
/// base (la cookie se valida contra ella y los formularios usan claves guardadas allí). Los demás errores siguen a /error.
/// </summary>
public sealed class DatabaseUnavailableHandler(ILogger<DatabaseUnavailableHandler> logger) : IExceptionHandler
{
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(30);

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (!IsDatabaseUnavailable(exception, out var retryAfter) || context.Response.HasStarted)
        {
            return false;
        }

        logger.LogWarning(exception, "La base de datos no está disponible: se respondió 503 a {Method} {Path}.", context.Request.Method, context.Request.Path);

        var seconds = (int)Math.Ceiling(Math.Max(retryAfter.TotalSeconds, 5));
        var requestId = Activity.Current?.Id ?? context.TraceIdentifier;
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(Page(requestId, seconds), cancellationToken);
        return true;
    }

    /// <summary>¿La excepción (o alguna interna) indica que la base no responde?</summary>
    public static bool IsDatabaseUnavailable(Exception exception, out TimeSpan retryAfter)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case DatabaseUnavailableException unavailable:
                    retryAfter = unavailable.RetryAfter;
                    return true;
                case RetryLimitExceededException:
                    retryAfter = DefaultRetryAfter;
                    return true;
            }

            // Data Protection envuelve en CryptographicException el error al leer sus claves de la base.
            if (current is not CryptographicException && DatabaseAvailability.IsConnectionFailure(current))
            {
                retryAfter = DefaultRetryAfter;
                return true;
            }
        }

        retryAfter = TimeSpan.Zero;
        return false;
    }

    private static string Page(string requestId, int seconds) => $$"""
        <!DOCTYPE html>
        <html lang="es">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>Servicio no disponible · FixTrack</title>
            <link rel="stylesheet" href="/css/site.css" />
        </head>
        <body>
            <main class="error-page error-page--standalone" aria-labelledby="error-title">
                <div class="empty-state__icon">
                    <svg class="icon" aria-hidden="true" focusable="false"><use href="/icons/icons.svg#server-off"></use></svg>
                </div>
                <p class="error-page__code mono">Error 503</p>
                <h1 class="error-page__title" id="error-title">Servicio temporalmente no disponible</h1>
                <p class="error-page__message">
                    FixTrack no puede conectarse a la base de datos en este momento. Espera unos {{seconds}} segundos y vuelve a
                    intentarlo; tu trabajo guardado no se pierde.
                </p>
                <p class="error-page__request">Identificador de la solicitud: <span class="mono">{{HtmlEncoder.Default.Encode(requestId)}}</span></p>
                <a class="button button--secondary" href="/">Reintentar</a>
            </main>
        </body>
        </html>
        """;
}
