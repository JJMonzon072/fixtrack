using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;

namespace FixTrack.Web.Portfolio;

/// <summary>
/// Quita del modelo de MVC los controladores de los extras desactivados. Sin controlador no hay acciones ni rutas: una
/// solicitud a <c>/reportes</c> o a <c>/api/v1/…</c> recibe la misma respuesta que cualquier ruta inexistente (inicio de
/// sesión sin sesión, 404 con sesión), sin revelar que el extra existe ni ejecutar su autorización.
/// </summary>
public sealed class PortfolioFeatureConvention(PortfolioFeatures features) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers.ToList())
        {
            var marker = controller.Attributes.OfType<PortfolioFeatureAttribute>().FirstOrDefault();
            if (marker is not null && !features.IsEnabled(marker.Feature))
            {
                application.Controllers.Remove(controller);
            }
        }
    }
}

/// <summary>Agrega <see cref="PortfolioFeatureConvention"/> a MVC con los valores de la configuración.</summary>
internal sealed class PortfolioMvcSetup(IOptions<PortfolioFeatures> features) : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options) => options.Conventions.Add(new PortfolioFeatureConvention(features.Value));
}
