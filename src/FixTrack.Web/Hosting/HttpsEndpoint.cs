namespace FixTrack.Web.Hosting;

/// <summary>Determina si la aplicación se inició con un endpoint HTTPS configurado.</summary>
public static class HttpsEndpoint
{
    /// <summary>
    /// Revisa las mismas fuentes que usa Kestrel: <c>urls</c> (ASPNETCORE_URLS, <c>--urls</c> o el
    /// perfil de inicio), <c>https_ports</c>, <c>https_port</c> y los endpoints de la sección <c>Kestrel</c>.
    /// </summary>
    public static bool IsConfigured(IConfiguration configuration) =>
        ContainsHttpsUrl(configuration["urls"])
        || !string.IsNullOrWhiteSpace(configuration["https_ports"])
        || !string.IsNullOrWhiteSpace(configuration["https_port"])
        || configuration.GetSection("Kestrel:Endpoints").GetChildren().Any(endpoint => ContainsHttpsUrl(endpoint["Url"]));

    private static bool ContainsHttpsUrl(string? urls) =>
        urls?.Contains("https://", StringComparison.OrdinalIgnoreCase) == true;
}
