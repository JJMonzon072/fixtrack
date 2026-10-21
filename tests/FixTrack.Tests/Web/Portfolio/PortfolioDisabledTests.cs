using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web.Portfolio;

/// <summary>
/// Con los extras desactivados (la configuración predeterminada), FixTrack es la versión académica: las rutas de los
/// extras no existen y responden exactamente igual que cualquier ruta inexistente, sin revelar nada.
/// </summary>
public sealed partial class PortfolioDisabledTests(FixTrackWebApplicationFactory factory) : IClassFixture<FixTrackWebApplicationFactory>
{
    private const string UnknownRoute = "/ruta-inexistente-de-control";

    public static TheoryData<string> ExtraRoutes { get; } =
    [
        "/reportes", "/reportes?rango=7d", "/reportes/exportar?por=entrega",
        "/fichas/recepcion/1", "/fichas/entrega/1",
        "/api/v1/ordenes", "/api/v1/ordenes/1", "/api/v1/equipos", "/api/v1/metricas",
    ];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Quita lo que cambia en cada respuesta (nonce, tokens) y la ruta pedida.</summary>
    private static string Normalize(string html, string path) =>
        Nonce().Replace(Token_().Replace(html.Replace(path, "{ruta}", StringComparison.Ordinal), "token"), "nonce");

    [Theory]
    [MemberData(nameof(ExtraRoutes))]
    public async Task With_a_session_each_route_answers_like_an_unknown_route(string url)
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var expected = await client.GetAsync(UnknownRoute, Token);
        using var actual = await client.GetAsync(url, Token);

        Assert.Equal(HttpStatusCode.NotFound, actual.StatusCode);
        Assert.Equal(expected.Content.Headers.ContentType?.ToString(), actual.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            Normalize(await expected.Content.ReadAsStringAsync(Token), UnknownRoute),
            Normalize(await actual.Content.ReadAsStringAsync(Token), url));
    }

    [Theory]
    [MemberData(nameof(ExtraRoutes))]
    public async Task Without_a_session_each_route_goes_to_the_login_like_an_unknown_route(string url)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var expected = await client.GetAsync(UnknownRoute, Token);
        using var actual = await client.GetAsync(url, Token);
        using var json = new HttpRequestMessage(HttpMethod.Get, url);
        json.Headers.Accept.ParseAdd("application/json");
        using var jsonActual = await client.SendAsync(json, Token);

        Assert.Equal(expected.StatusCode, actual.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, actual.StatusCode);
        Assert.Equal(
            expected.Headers.Location!.ToString().Replace(Uri.EscapeDataString(UnknownRoute), "{ruta}", StringComparison.Ordinal),
            actual.Headers.Location!.ToString().Replace(Uri.EscapeDataString(url), "{ruta}", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, jsonActual.StatusCode);
    }

    [Fact]
    public void No_endpoint_of_the_extras_is_registered()
    {
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToList();

        Assert.NotEmpty(routes);
        Assert.DoesNotContain(routes, route =>
            route.StartsWith("reportes", StringComparison.Ordinal) || route.StartsWith("fichas", StringComparison.Ordinal)
            || route.StartsWith("api", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/ordenes")]
    [InlineData("/clientes")]
    [InlineData("/tecnicos")]
    public async Task Academic_screens_have_no_links_or_assets_of_the_extras(string url)
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var html = await client.GetStringAsync(url, Token);

        foreach (var marker in new[] { "/reportes", "/fichas/", "/api/", "Análisis", "reports.css", "portfolio-icons.svg", "Ficha de recepción", "Comprobante de entrega" })
        {
            Assert.DoesNotContain(marker, html, StringComparison.Ordinal);
        }
    }

    [GeneratedRegex("nonce=\"[^\"]+\"|nonce-[A-Za-z0-9_-]+")]
    private static partial Regex Nonce();

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"[^\"]+\"")]
    private static partial Regex Token_();
}
