using System.Net;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FixTrack.Tests.Web;

/// <summary>Ejecución en un contenedor detrás de un proxy HTTPS: salud, encabezados reenviados y redirección.</summary>
public sealed class HostingTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    /// <summary>Producción con el proxy confiable configurado y la conexión simulada desde <paramref name="remoteIp"/>.</summary>
    private WebApplicationFactory<Program> BehindProxy(string remoteIp, bool trustProxy = true) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production").UseSetting("https_port", "443");
            if (trustProxy)
            {
                builder.UseSetting("ReverseProxy:KnownNetworks:0", "10.0.0.0/8");
            }

            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(IPAddress.Parse(remoteIp))));
        });

    [Theory]
    [InlineData("/salud")]
    [InlineData("/salud/bd")]
    public async Task Health_endpoints_answer_without_a_session_and_without_details(string url)
    {
        using var client = factory.CreateClient(NoRedirects);

        using var response = await client.GetAsync(url, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Token));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_failing_check_reports_only_its_status_without_details()
    {
        using var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services
                .AddHealthChecks()
                .AddCheck("falla-simulada", () => HealthCheckResult.Unhealthy("detalle interno: Server=sql;Password=x"), tags: ["bd"])))
            .CreateClient(NoRedirects);

        using var database = await client.GetAsync("/salud/bd", Token);
        using var liveness = await client.GetAsync("/salud", Token);
        var body = await database.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, database.StatusCode);
        Assert.Equal("Unhealthy", body);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
    }

    [Fact]
    public async Task The_database_check_is_unhealthy_when_the_database_cannot_be_opened()
    {
        await using var context = new FixTrackDbContext(new DbContextOptionsBuilder<FixTrackDbContext>()
            .UseSqlite("Data Source=/ruta/inexistente/fixtrack.db;Mode=ReadOnly")
            .Options);

        var result = await new DatabaseHealthCheck(context).CheckHealthAsync(new HealthCheckContext(), Token);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task In_production_health_checks_are_not_redirected_to_https_but_pages_are()
    {
        using var client = factory
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production").UseSetting("https_port", "443"))
            .CreateClient(NoRedirects);

        using var health = await client.GetAsync("/salud", Token);
        using var page = await client.GetAsync("/cuenta/iniciar-sesion", Token);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, page.StatusCode);
        Assert.Equal("https://localhost/cuenta/iniciar-sesion", page.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_trusted_proxy_can_report_that_the_original_request_was_https()
    {
        using var client = BehindProxy("10.20.30.40").CreateClient(NoRedirects);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/cuenta/iniciar-sesion");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");

        using var response = await client.SendAsync(request, Token);

        // Sin el proxy confiable, la misma solicitud HTTP se redirigiría a HTTPS (ver la prueba siguiente).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("203.0.113.9", true)]
    [InlineData("10.20.30.40", false)]
    public async Task Forwarded_headers_from_untrusted_sources_are_ignored(string remoteIp, bool trustProxy)
    {
        using var client = BehindProxy(remoteIp, trustProxy).CreateClient(NoRedirects);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/cuenta/iniciar-sesion");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
    }

    /// <summary>Simula la dirección del extremo que abre la conexión (en TestServer no existe).</summary>
    private sealed class RemoteAddressFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
