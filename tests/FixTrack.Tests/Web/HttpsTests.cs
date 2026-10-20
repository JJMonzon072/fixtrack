using System.Collections.Concurrent;
using System.Net;
using FixTrack.Web.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FixTrack.Tests.Web;

/// <summary>Configuración de HTTPS según el entorno y los endpoints configurados.</summary>
public sealed class HttpsTests(FixTrackWebApplicationFactory factory)
    : IClassFixture<FixTrackWebApplicationFactory>
{
    private const string HttpsRedirectionCategory = "Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("urls", "https://localhost:7171;http://localhost:5104", true)]
    [InlineData("urls", "HTTPS://localhost:7171", true)]
    [InlineData("https_ports", "8443", true)]
    [InlineData("https_port", "443", true)]
    [InlineData("Kestrel:Endpoints:Https:Url", "https://localhost:7171", true)]
    [InlineData("urls", "http://localhost:5104", false)]
    [InlineData("Kestrel:Endpoints:Http:Url", "http://localhost:5104", false)]
    public void Detects_whether_an_https_endpoint_is_configured(string key, string value, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

        Assert.Equal(expected, HttpsEndpoint.IsConfigured(configuration));
    }

    [Fact]
    public void No_configuration_means_no_https_endpoint()
    {
        Assert.False(HttpsEndpoint.IsConfigured(new ConfigurationBuilder().Build()));
    }

    [Fact]
    public async Task Development_over_http_does_not_redirect_or_log_https_warnings()
    {
        var logs = new CapturingLoggerProvider();
        using var client = factory
            .WithWebHostBuilder(builder => builder
                .UseEnvironment("Development")
                .ConfigureLogging(logging => logging.AddProvider(logs)))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/cuenta/iniciar-sesion", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(logs.Entries, entry =>
            entry.Category == HttpsRedirectionCategory && entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Development_with_an_https_profile_redirects_to_https()
    {
        using var client = factory
            .WithWebHostBuilder(builder => builder
                .UseEnvironment("Development")
                .UseSetting("urls", "https://localhost:7171;http://localhost:5104")
                .UseSetting("https_port", "7171"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/", CancellationToken);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://localhost:7171/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Production_redirects_http_requests_to_https()
    {
        using var client = factory
            .WithWebHostBuilder(builder => builder
                .UseEnvironment("Production")
                .UseSetting("https_port", "443")
                .UseSetting("ReverseProxy:IgnoreForwardedHeaders", "true"))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/clientes", CancellationToken);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://localhost/clientes", response.Headers.Location?.ToString());
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    /// <summary>Guarda en memoria los mensajes registrados por la aplicación.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}
