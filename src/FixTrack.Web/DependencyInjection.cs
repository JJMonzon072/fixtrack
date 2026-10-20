using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.WebEncoders;

namespace FixTrack.Web;

public static class DependencyInjection
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es");

    /// <summary>Registra los servicios de la capa de presentación.</summary>
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        // Toda solicitud POST, PUT, PATCH o DELETE debe incluir el token antifalsificación (CSRF).
        services.AddControllersWithViews(options =>
        {
            options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            UseSpanishBindingMessages(options.ModelBindingMessageProvider);
        });
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
        services.AddSingleton<DateDisplay>();

        return services;
    }

    /// <summary>
    /// Mensajes en español para los valores que no pueden convertirse al tipo del campo (por ejemplo, una prioridad
    /// o un identificador que no es un número). La validación de negocio la hace la capa de aplicación.
    /// </summary>
    private static void UseSpanishBindingMessages(DefaultModelBindingMessageProvider messages)
    {
        messages.SetAttemptedValueIsInvalidAccessor((value, _) => $"El valor «{value}» no es válido.");
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"El valor «{value}» no es válido.");
        messages.SetUnknownValueIsInvalidAccessor(_ => "El valor indicado no es válido.");
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "El valor indicado no es válido.");
        messages.SetValueIsInvalidAccessor(value => $"El valor «{value}» no es válido.");
        messages.SetValueMustBeANumberAccessor(_ => "Escribe un número.");
        messages.SetNonPropertyValueMustBeANumberAccessor(() => "Escribe un número.");
        messages.SetValueMustNotBeNullAccessor(_ => "Este dato es obligatorio.");
        messages.SetMissingBindRequiredValueAccessor(_ => "Falta un dato obligatorio.");
        messages.SetMissingKeyOrValueAccessor(() => "Falta un dato obligatorio.");
        messages.SetMissingRequestBodyRequiredValueAccessor(() => "La solicitud no contiene datos.");
    }
}
