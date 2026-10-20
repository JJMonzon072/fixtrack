using System.Diagnostics;
using FixTrack.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>Páginas de error. Atiende las excepciones no controladas y los códigos de estado HTTP.</summary>
[Route("error")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorController : Controller
{
    // Sin verbo HTTP específico: el middleware reejecuta la solicitud original con su mismo método.
    [Route("")]
    public IActionResult Index() => Render(StatusCodes.Status500InternalServerError);

    [Route("{statusCode:int}")]
    public IActionResult Status(int statusCode) =>
        Render(statusCode is >= 400 and <= 599 ? statusCode : StatusCodes.Status404NotFound);

    private ViewResult Render(int statusCode)
    {
        Response.StatusCode = statusCode;

        var model = statusCode switch
        {
            StatusCodes.Status404NotFound => new ErrorViewModel(
                statusCode,
                "Página no encontrada",
                "La dirección que buscas no existe o ya no está disponible.",
                "compass",
                RequestId: null),
            >= 500 => new ErrorViewModel(
                statusCode,
                "Ocurrió un error inesperado",
                "No pudimos completar la solicitud. Intenta de nuevo y, si el problema continúa, comunícate con el administrador del sistema.",
                "triangle-alert",
                Activity.Current?.Id ?? HttpContext.TraceIdentifier),
            _ => new ErrorViewModel(
                statusCode,
                "No se pudo completar la solicitud",
                "Revisa la dirección e intenta de nuevo.",
                "triangle-alert",
                RequestId: null),
        };

        ViewData["Title"] = model.Title;
        return View("Error", model);
    }
}
