using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FixTrack.Tests.Web;

/// <summary>Pruebas de integración: levantan la aplicación en memoria y solicitan sus páginas.</summary>
public sealed partial class PageTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/", "Panel general")]
    [InlineData("/ordenes", "Órdenes de servicio")]
    [InlineData("/cola-de-trabajo", "Cola de trabajo")]
    [InlineData("/clientes", "Clientes")]
    [InlineData("/equipos", "Equipos")]
    [InlineData("/tecnicos", "Técnicos")]
    public async Task Main_pages_respond_successfully_in_spanish(string url, string expectedHeading)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(url, CancellationToken);
        var html = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<html lang=\"es\">", html);
        Assert.Contains($">{expectedHeading}</h1>", html);
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/ordenes", "/ordenes")]
    [InlineData("/ordenes/nueva", "/ordenes")]
    [InlineData("/cola-de-trabajo", "/cola-de-trabajo")]
    [InlineData("/clientes", "/clientes")]
    [InlineData("/equipos", "/equipos")]
    [InlineData("/tecnicos", "/tecnicos")]
    public async Task Sidebar_marks_only_the_current_module_as_active(string url, string expectedHref)
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(url, CancellationToken);
        var activeLinks = ActiveNavigationLink().Matches(html);

        var activeLink = Assert.Single(activeLinks);
        Assert.Contains($"href=\"{expectedHref}\"", activeLink.Value);
    }

    [Fact]
    public async Task Dashboard_metrics_are_numbers_from_the_database_not_placeholders()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", CancellationToken);
        var values = MetricValue().Matches(html).Select(match => match.Groups["value"].Value).ToList();

        Assert.Equal(6, values.Count);
        Assert.All(values, value => Assert.Matches(@"^\d+$", value));
        Assert.DoesNotContain("Sin datos", html);
    }

    [Fact]
    public async Task Unknown_routes_return_a_not_found_page()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/ruta-inexistente", CancellationToken);
        var html = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Página no encontrada", html);
    }

    [Theory]
    [InlineData("/css/site.css", "text/css")]
    [InlineData("/js/site.js", "text/javascript")]
    [InlineData("/icons/icons.svg", "image/svg+xml")]
    [InlineData("/fonts/ibm-plex-sans-latin-wght.woff2", "font/woff2")]
    public async Task Static_assets_are_served(string url, string expectedMediaType)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(url, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedMediaType, response.Content.Headers.ContentType?.MediaType);
    }

    [GeneratedRegex("<a class=\"nav-link\"[^>]*aria-current=\"page\"[^>]*>")]
    private static partial Regex ActiveNavigationLink();

    [GeneratedRegex("<dd class=\"metric__value\">(?<value>[^<]*)</dd>")]
    private static partial Regex MetricValue();
}
