using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.WebEncoders;

namespace FixTrack.Web;

public static class DependencyInjection
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");

    /// <summary>Registra los servicios de la capa de presentación.</summary>
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddControllersWithViews();
        services.AddRouting(options => options.LowercaseUrls = true);

        // Toda la interfaz está en español: fechas y números se formatean con esa cultura.
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(Spanish);
            options.SupportedCultures = [Spanish];
            options.SupportedUICultures = [Spanish];
        });

        // Evita que el HTML generado escape tildes y eñes como entidades numéricas.
        services.Configure<WebEncoderOptions>(options =>
            options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

        services.AddSingleton<ApplicationInfo>();

        return services;
    }
}
