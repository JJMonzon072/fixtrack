using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Tests.Application;
using FixTrack.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace FixTrack.Tests.Web;

/// <summary>Ejecución en un contenedor detrás de un proxy HTTPS: salud, encabezados reenviados y redirección.</summary>
public sealed class HostingTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    /// <summary>Como el navegador detrás del proxy: HTTPS, para que las cookies Secure viajen.</summary>
    private static readonly WebApplicationFactoryClientOptions Https = new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") };

    /// <summary>Producción con el proxy confiable configurado y la conexión simulada desde <paramref name="remoteIp"/>.</summary>
    private WebApplicationFactory<Program> BehindProxy(string remoteIp, bool trustProxy = true, Action<IWebHostBuilder>? configure = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            configure?.Invoke(builder);
            builder.UseEnvironment("Production").UseSetting("https_port", "443");
            builder.UseSetting(trustProxy ? "ReverseProxy:KnownNetworks:0" : "ReverseProxy:IgnoreForwardedHeaders", trustProxy ? "10.0.0.0/8" : "true");

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

        var result = await new DatabaseHealthCheck(context, new DatabaseHealthCache(TimeProvider.System)).CheckHealthAsync(new HealthCheckContext(), Token);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task In_production_health_checks_are_not_redirected_to_https_but_pages_are()
    {
        using var client = factory
            .WithWebHostBuilder(builder => builder
                .UseEnvironment("Production")
                .UseSetting("https_port", "443")
                .UseSetting("ReverseProxy:IgnoreForwardedHeaders", "true"))
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

    [Fact]
    public void Production_does_not_start_without_deciding_which_proxy_to_trust()
    {
        var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var error = Assert.ThrowsAny<Exception>(() => production.CreateClient());

        Assert.Contains("ReverseProxy__KnownNetworks__0", MessagesOf(error));
        Assert.Contains("ReverseProxy__IgnoreForwardedHeaders=true", MessagesOf(error));
    }

    [Theory]
    [InlineData("ReverseProxy:KnownNetworks:0", "10.0.0.0/33", "contiene un valor no válido")]
    [InlineData("ReverseProxy:KnownProxies:0", "no-es-una-ip", "contiene un valor no válido")]
    public void Invalid_proxy_addresses_stop_the_application(string key, string value, string expected)
    {
        var invalid = factory.WithWebHostBuilder(builder => builder.UseSetting(key, value));

        Assert.Contains(expected, MessagesOf(Assert.ThrowsAny<Exception>(() => invalid.CreateClient())));
    }

    [Fact]
    public void Trusting_a_proxy_and_ignoring_forwarded_headers_at_once_is_rejected()
    {
        var contradictory = factory.WithWebHostBuilder(builder => builder
            .UseSetting("ReverseProxy:KnownProxies:0", "10.1.2.3")
            .UseSetting("ReverseProxy:IgnoreForwardedHeaders", "true"));

        Assert.Contains("no puede combinarse", MessagesOf(Assert.ThrowsAny<Exception>(() => contradictory.CreateClient())));
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_each_client_gets_its_own_login_quota_and_spoofed_entries_are_ignored()
    {
        using var client = BehindProxy("10.20.30.40", configure: builder => builder.UseSetting("Security:LoginAttemptsPerMinute", "2"))
            .CreateClient(Https);

        // El cliente cambia la parte izquierda de X-Forwarded-For en cada intento; el proxy agrega su IP real al final.
        var first = await LoginStatusesAsync(client, "X-Forwarded-For", "198.51.100.1, 203.0.113.7", "198.51.100.2, 203.0.113.7", "198.51.100.3, 203.0.113.7");
        var other = await LoginStatusesAsync(client, "X-Forwarded-For", "203.0.113.8");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], first);
        Assert.Equal([HttpStatusCode.OK], other);
    }

    [Fact]
    public async Task A_single_value_client_ip_header_from_the_edge_can_be_used_instead()
    {
        using var client = BehindProxy(
                "10.20.30.40",
                configure: builder => builder
                    .UseSetting("Security:LoginAttemptsPerMinute", "1")
                    .UseSetting("ReverseProxy:ClientIpHeader", "True-Client-IP"))
            .CreateClient(Https);

        var first = await LoginStatusesAsync(client, "True-Client-IP", "203.0.113.20", "203.0.113.20");
        var other = await LoginStatusesAsync(client, "True-Client-IP", "203.0.113.21");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], first);
        Assert.Equal([HttpStatusCode.OK], other);
    }

    [Fact]
    public async Task Forwarded_headers_from_an_untrusted_address_are_logged_once_without_their_values()
    {
        var logs = new LogCapture();
        using var client = BehindProxy(
                "10.99.0.7",
                trustProxy: false,
                configure: builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)))
            .CreateClient(NoRedirects);

        foreach (var _ in Enumerable.Range(0, 3))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/salud");
            request.Headers.Add("X-Forwarded-For", "203.0.113.50");
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await client.SendAsync(request, Token);
        }

        var warnings = logs.Messages.Where(message => message.Contains("Se ignoraron encabezados reenviados", StringComparison.Ordinal)).ToList();
        Assert.Single(warnings);
        Assert.Contains("10.99.0.7", warnings[0]);
        Assert.DoesNotContain("203.0.113.50", warnings[0]);
    }

    [Fact]
    public async Task An_ipv4_address_in_ipv6_notation_is_logged_as_ipv4_so_it_can_be_copied_to_the_configuration()
    {
        // Docker Desktop (y otras plataformas) entregan la conexión como ::ffff:a.b.c.d.
        var logs = new LogCapture();
        using var client = BehindProxy("::ffff:10.99.0.8", trustProxy: false, configure: builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)))
            .CreateClient(NoRedirects);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/salud");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request, Token);

        var warning = Assert.Single(logs.Messages, message => message.Contains("Se ignoraron encabezados reenviados", StringComparison.Ordinal));
        Assert.Contains("desde 10.99.0.8:", warning);
        Assert.DoesNotContain("::ffff:", warning);
    }

    [Theory]
    [InlineData("::ffff:10.20.30.41", "::ffff:10.20.30.41")]
    [InlineData("::ffff:10.20.30.41", "10.20.30.41")]
    [InlineData("10.20.30.41", "::ffff:10.20.30.41")]
    public async Task A_proxy_is_trusted_in_either_notation_and_is_no_longer_reported(string configured, string remoteIp)
    {
        var logs = new LogCapture();
        using var client = factory.WithWebHostBuilder(builder => builder
                .UseEnvironment("Production")
                .UseSetting("https_port", "443")
                .UseSetting("ReverseProxy:KnownProxies:0", configured)
                .ConfigureLogging(logging => logging.AddProvider(logs))
                .ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(IPAddress.Parse(remoteIp)))))
            .CreateClient(NoRedirects);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/cuenta/iniciar-sesion");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        request.Headers.Host = "fixtrack.example"; // HSTS nunca se envía a localhost

        using var response = await client.SendAsync(request, Token);

        // Sin confiar en el proxy, la solicitud HTTP se redirigiría a HTTPS y se registraría la advertencia.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
        Assert.DoesNotContain(logs.Messages, message => message.Contains("Se ignoraron encabezados reenviados", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_database_status_is_reused_for_a_few_seconds_so_the_endpoint_cannot_flood_the_database()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var cache = new DatabaseHealthCache(clock);
        var checks = 0;
        Task<HealthCheckResult> Check(CancellationToken _)
        {
            Interlocked.Increment(ref checks);
            return Task.FromResult(HealthCheckResult.Healthy());
        }

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetAsync(Check, Token)));
        clock.Advance(DatabaseHealthCache.Lifetime - TimeSpan.FromSeconds(1));
        await cache.GetAsync(Check, Token);
        var withinLifetime = checks;
        clock.Advance(TimeSpan.FromSeconds(2));
        await cache.GetAsync(Check, Token);

        Assert.Equal(1, withinLifetime);
        Assert.Equal(2, checks);
    }

    [Fact]
    public async Task The_database_check_runs_a_real_query()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var check = new DatabaseHealthCheck(scope.ServiceProvider.GetRequiredService<FixTrackDbContext>(), new DatabaseHealthCache(TimeProvider.System));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), Token);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void Production_does_not_log_every_sql_command()
    {
        using var production = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("ReverseProxy:IgnoreForwardedHeaders", "true"));

        var configuration = production.Services.GetRequiredService<IConfiguration>();

        Assert.Equal("Warning", configuration["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"]);
    }

    private static async Task<List<HttpStatusCode>> LoginStatusesAsync(HttpClient client, string header, params string[] values)
    {
        var statuses = new List<HttpStatusCode>();
        foreach (var value in values)
        {
            // Un intento real: el formulario con su token. Sin token, la antifalsificación responde 400 antes de contar.
            using var page = new HttpRequestMessage(HttpMethod.Get, "/cuenta/iniciar-sesion");
            page.Headers.Add(header, value);
            using var pageResponse = await client.SendAsync(page, Token);
            var html = await pageResponse.Content.ReadAsStringAsync(Token);
            var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"").Groups["token"].Value;

            using var request = new HttpRequestMessage(HttpMethod.Post, "/cuenta/iniciar-sesion")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Email"] = "nadie@fixtrack.test",
                    ["Password"] = "Cualquier1234",
                    ["__RequestVerificationToken"] = token,
                }),
            };
            request.Headers.Add(header, value);
            using var response = await client.SendAsync(request, Token);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    private static string MessagesOf(Exception error)
    {
        var messages = new List<string>();
        for (var current = error; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private sealed class LogCapture : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new Logger(_messages);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
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
