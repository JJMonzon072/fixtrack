using FixTrack.Web.Controllers;
using FixTrack.Web.Hosting;
using FixTrack.Web.Portfolio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;

namespace FixTrack.Web.Api;

/// <summary>
/// Base de los controladores de la API REST v1 (extra de portafolio, <c>PortfolioFeatures:ReadOnlyApi</c>). Solo lectura,
/// con la sesión de FixTrack y únicamente para Administrador y Operador: el rol Consulta, pensado para cuentas
/// compartidas de demostración, no puede extraer datos en bloque por la API. Sin CORS: solo el propio sitio puede leer
/// las respuestas con la cookie de sesión.
/// </summary>
[ApiController]
[Authorize(Roles = ReportsController.ExportRoles)]
[EnableRateLimiting(PortfolioExtensions.ApiRateLimit)]
[ApiExceptionFilter]
public abstract class ApiV1Controller : ControllerBase
{
    public const string RoutePrefix = "api/v1";

    /// <summary>Respuesta correcta en JSON con las opciones de la API.</summary>
    protected static JsonResult Data(object value) =>
        new(value, ApiProblem.JsonOptions) { ContentType = ApiProblem.JsonContentType, StatusCode = StatusCodes.Status200OK };

    /// <summary>Respuesta de error en formato Problem Details.</summary>
    protected JsonResult Problem(int statusCode, string? title = null, string? detail = null) =>
        new(ApiProblem.Create(HttpContext, statusCode, title, detail), ApiProblem.JsonOptions)
        {
            StatusCode = statusCode,
            ContentType = ApiProblem.ContentType,
        };

    /// <summary>400 con los errores de cada parámetro.</summary>
    protected JsonResult Invalid(ApiParameters parameters)
    {
        var problem = new ValidationProblemDetails(parameters.Errors.ToDictionary(error => error.Key, error => error.Value))
        {
            Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1",
            Title = "Parámetros no válidos",
            Status = StatusCodes.Status400BadRequest,
            Detail = "Revisa los parámetros indicados en errors.",
        };
        problem.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return new JsonResult(problem, ApiProblem.JsonOptions) { StatusCode = StatusCodes.Status400BadRequest, ContentType = ApiProblem.ContentType };
    }
}

/// <summary>
/// Convierte los errores no controlados de la API en Problem Details sin detalles técnicos: 503 si la base no responde
/// (con <c>Retry-After</c>) y 500 en otro caso. El detalle queda en la bitácora con el identificador de la solicitud.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ApiExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        var http = context.HttpContext;
        if (context.Exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            return;
        }

        var logger = http.RequestServices.GetRequiredService<ILogger<ApiExceptionFilterAttribute>>();
        int statusCode;
        if (DatabaseUnavailableHandler.IsDatabaseUnavailable(context.Exception, out var retryAfter))
        {
            statusCode = StatusCodes.Status503ServiceUnavailable;
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(Math.Max(retryAfter.TotalSeconds, 5))).ToString(System.Globalization.CultureInfo.InvariantCulture);
            logger.LogWarning("API: la base de datos no respondió; se respondió 503 a {Method} {Path}.", http.Request.Method, http.Request.Path.Value);
        }
        else
        {
            statusCode = StatusCodes.Status500InternalServerError;
            logger.LogError(context.Exception, "API: error no controlado en {Method} {Path}.", http.Request.Method, http.Request.Path.Value);
        }

        http.Response.Headers.CacheControl = "no-store";
        context.Result = new JsonResult(ApiProblem.Create(http, statusCode), ApiProblem.JsonOptions)
        {
            StatusCode = statusCode,
            ContentType = ApiProblem.ContentType,
        };
        context.ExceptionHandled = true;
    }
}
