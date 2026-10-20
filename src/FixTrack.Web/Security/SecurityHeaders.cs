using System.Security.Cryptography;

namespace FixTrack.Web.Security;

/// <summary>
/// Encabezados de seguridad de todas las respuestas. La política de contenido (CSP) solo permite recursos del propio
/// sitio; el único script en línea (aplicar el tema antes de pintar) lleva un nonce distinto en cada solicitud.
/// </summary>
public static class SecurityHeaders
{
    private const string NonceKey = "FixTrack.CspNonce";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            context.Items[NonceKey] = nonce;

            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy =
                "default-src 'self'; " +
                $"script-src 'self' 'nonce-{nonce}'; " +
                "style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; " +
                "form-action 'self'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'";
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            // Las páginas con datos del taller no se guardan en la caché del navegador (equipos compartidos).
            context.Response.OnStarting(() =>
            {
                var contentType = context.Response.ContentType;
                if (contentType is not null
                    && (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
                        || contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)))
                {
                    context.Response.Headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });

            await next(context);
        });

    /// <summary>Nonce de la solicitud para el atributo <c>nonce</c> de un script en línea.</summary>
    public static string CspNonce(this HttpContext context) => context.Items[NonceKey] as string ?? string.Empty;
}
