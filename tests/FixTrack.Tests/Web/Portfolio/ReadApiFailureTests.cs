using System.Net;
using System.Text.Json;
using FixTrack.Application.Common;
using FixTrack.Application.Devices;
using FixTrack.Application.ReadApi;
using FixTrack.Application.Reports;
using FixTrack.Application.ServiceOrders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FixTrack.Tests.Web.Portfolio;

/// <summary>Errores de la API: sin trazas técnicas, 503 cuando la base no responde y límite de solicitudes por sesión.</summary>
public sealed class ReadApiFailureTests(ReadApiFailureTests.Factory factory) : IClassFixture<ReadApiFailureTests.Factory>
{
    private const string SecretDetail = "detalle interno: cadena de conexión y pila";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_unexpected_error_answers_500_without_technical_details()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync("/api/v1/ordenes/500", Token);
        var body = await response.Content.ReadAsStringAsync(Token);
        var problem = JsonDocument.Parse(body).RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Error interno", problem.GetProperty("title").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
        foreach (var leak in new[] { SecretDetail, "InvalidOperationException", "at FixTrack", "StackTrace", "exception" })
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task An_unavailable_database_answers_503_with_retry_after()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync("/api/v1/ordenes/503", Token);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("30", response.Headers.RetryAfter?.ToString());
        Assert.Equal("Servicio no disponible", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Each_session_has_a_request_limit_per_minute()
    {
        using var client = await factory.CreateAuthenticatedClientAsync(TestAccounts.Operator);

        var statuses = new List<HttpStatusCode>();
        for (var request = 0; request < Factory.RequestsPerMinute + 1; request++)
        {
            using var response = await client.GetAsync("/api/v1/ordenes", Token);
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                Assert.Equal("Demasiadas solicitudes", problem.GetProperty("title").GetString());
            }
        }

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.OK, Factory.RequestsPerMinute), statuses.Take(Factory.RequestsPerMinute));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    /// <summary>API habilitada, con un servicio que falla a pedido y un límite bajo de solicitudes.</summary>
    public sealed class Factory : FixTrackWebApplicationFactory
    {
        public const int RequestsPerMinute = 5;

        protected override IEnumerable<KeyValuePair<string, string>> ExtraSettings =>
        [
            new("PortfolioFeatures:ReadOnlyApi", "true"),
            new("PortfolioFeatures:ApiRequestsPerMinute", RequestsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Scoped<IReadApiService, FailingReadApi>()));
        }
    }

    private sealed class FailingReadApi : IReadApiService
    {
        public Task<PagedResult<ServiceOrderListItem>> ListOrdersAsync(OrderListQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<ServiceOrderListItem>([], query.Page, query.PageSize, 0));

        public Task<ServiceOrderDetails?> GetOrderAsync(int id, CancellationToken cancellationToken) => id switch
        {
            500 => throw new InvalidOperationException(SecretDetail),
            503 => throw new DatabaseUnavailableException(SecretDetail, TimeSpan.FromSeconds(30)),
            _ => Task.FromResult<ServiceOrderDetails?>(null),
        };

        public Task<PagedResult<DeviceListItem>> ListDevicesAsync(DeviceListQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<DeviceListItem>([], query.Page, query.PageSize, 0));

        public Task<OperationsReport> GetMetricsAsync(ReportFilter filter, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
