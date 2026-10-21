using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Api;

/// <summary>
/// Errores de la API en formato Problem Details (RFC 9457), en español y sin trazas técnicas: solo el código, un título,
/// una explicación y el identificador de la solicitud para buscarla en la bitácora.
/// </summary>
public static class ApiProblem
{
    public const string ContentType = "application/problem+json";

    /// <summary>Tipo de las respuestas correctas.</summary>
    public const string JsonContentType = "application/json; charset=utf-8";

    /// <summary>
    /// Opciones JSON de la API (solo de la API: las de MVC no cambian). camelCase y texto en español legible (sin escapar
    /// tildes), pero con los caracteres sensibles en HTML (&lt; &gt; &amp; ' ") escapados.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>Prefijo de las rutas de la API.</summary>
    public const string PathPrefix = "/api";

    public static bool IsApiRequest(HttpContext context) => context.Request.Path.StartsWithSegments(PathPrefix);

    /// <summary>Título y explicación predeterminados de cada código.</summary>
    public static (string Title, string Detail) Describe(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ("Solicitud no válida", "Revisa los parámetros de la solicitud."),
        StatusCodes.Status401Unauthorized => ("Se requiere iniciar sesión", "La API usa la sesión de FixTrack: inicia sesión en la aplicación y vuelve a intentarlo."),
        StatusCodes.Status403Forbidden => ("Sin permiso", "La API de consulta está disponible solo para las cuentas de Administrador y Operador."),
        StatusCodes.Status404NotFound => ("No encontrado", "El recurso solicitado no existe."),
        StatusCodes.Status405MethodNotAllowed => ("Método no permitido", "La API v1 es de solo lectura: solo acepta GET."),
        StatusCodes.Status406NotAcceptable => ("Formato no disponible", "La API solo responde en JSON."),
        StatusCodes.Status429TooManyRequests => ("Demasiadas solicitudes", "Se superó el límite de solicitudes por minuto. Espera un momento y vuelve a intentarlo."),
        StatusCodes.Status503ServiceUnavailable => ("Servicio no disponible", "La base de datos no responde en este momento. Vuelve a intentarlo en unos segundos."),
        >= 500 => ("Error interno", "No se pudo completar la solicitud. Vuelve a intentarlo más tarde."),
        _ => ("Error en la solicitud", "No se pudo completar la solicitud."),
    };

    public static ProblemDetails Create(HttpContext context, int statusCode, string? title = null, string? detail = null)
    {
        var (defaultTitle, defaultDetail) = Describe(statusCode);
        var problem = new ProblemDetails
        {
            Type = TypeFor(statusCode),
            Title = title ?? defaultTitle,
            Status = statusCode,
            Detail = detail ?? defaultDetail,
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        return problem;
    }

    /// <summary>Escribe el error directamente en la respuesta (fuera de MVC: autenticación, rutas y métodos).</summary>
    public static async Task WriteAsync(HttpContext context, int statusCode)
    {
        var response = context.Response;
        response.StatusCode = statusCode;
        response.ContentType = ContentType;
        response.Headers.CacheControl = "no-store";
        await JsonSerializer.SerializeAsync(response.Body, Create(context, statusCode), JsonOptions, CancellationToken.None);
    }

    /// <summary>Referencia de la sección del código en RFC 9110.</summary>
    private static string TypeFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1",
        StatusCodes.Status401Unauthorized => "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.2",
        StatusCodes.Status403Forbidden => "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.4",
        StatusCodes.Status404NotFound => "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.5",
        StatusCodes.Status405MethodNotAllowed => "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.6",
        StatusCodes.Status406NotAcceptable => "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.7",
        StatusCodes.Status429TooManyRequests => "https://www.rfc-editor.org/rfc/rfc6585#section-4",
        StatusCodes.Status503ServiceUnavailable => "https://www.rfc-editor.org/rfc/rfc9110#section-15.6.4",
        _ => "https://www.rfc-editor.org/rfc/rfc9110#section-15.6.1",
    };
}
