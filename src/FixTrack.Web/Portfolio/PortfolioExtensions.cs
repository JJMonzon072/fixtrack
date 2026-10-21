using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FixTrack.Web.Portfolio;

public static class PortfolioExtensions
{
    /// <summary>
    /// Registra los extras de portafolio. Sus servicios existen siempre (no cambian nada mientras nadie los use); sus
    /// controladores solo se registran si el extra está habilitado en <c>PortfolioFeatures</c>.
    /// </summary>
    public static IServiceCollection AddPortfolioExtras(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PortfolioFeatures>()
            .Bind(configuration.GetSection(PortfolioFeatures.SectionName))
            .Validate(features => features.ExportMaxRows is > 0 and <= 50_000, "PortfolioFeatures:ExportMaxRows debe estar entre 1 y 50000.")
            .ValidateOnStart();
        services.AddTransient<IConfigureOptions<MvcOptions>, PortfolioMvcSetup>();
        return services;
    }

    /// <summary>
    /// Deja constancia en la bitácora de los extras habilitados. Con todos desactivados no agrega nada al proceso de las
    /// solicitudes.
    /// </summary>
    public static WebApplication UsePortfolioExtras(this WebApplication app)
    {
        var features = app.Services.GetRequiredService<IOptions<PortfolioFeatures>>().Value;
        if (features.EnabledNames() is { Count: > 0 } enabled)
        {
            app.Logger.LogInformation("Extras de portafolio habilitados: {Features}.", string.Join(", ", enabled));
        }

        return app;
    }
}
