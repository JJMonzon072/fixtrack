using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using FixTrack.Application.Common;
using FixTrack.Infrastructure;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Infrastructure.Persistence.Resilience;
using FixTrack.Tests.Application;
using FixTrack.Web.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FixTrack.Tests.Web;

/// <summary>Fallos temporales de la base: disyuntor, tiempos máximos y página 503 en lugar de un error inesperado.</summary>
public sealed class ResilienceTests(FixTrackWebApplicationFactory factory) : IClassFixture<FixTrackWebApplicationFactory>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void The_breaker_opens_after_repeated_connection_failures_and_closes_after_a_while()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var availability = new DatabaseAvailability(clock);

        availability.RecordFailure(new TimeoutException());
        availability.RecordFailure(new InvalidOperationException("Error de la consulta, no de la conexión."));
        availability.RecordFailure(new TimeoutException());
        var openBeforeThreshold = availability.IsOpen(out _);
        availability.RecordFailure(new TimeoutException());
        var open = availability.IsOpen(out var retryAfter);
        clock.Advance(DatabaseAvailability.BreakDuration);
        var openAfterBreak = availability.IsOpen(out _);

        Assert.False(openBeforeThreshold);
        Assert.True(open);
        Assert.Equal(DatabaseAvailability.BreakDuration, retryAfter);
        Assert.False(openAfterBreak);
    }

    [Fact]
    public void A_successful_connection_resets_the_failure_count()
    {
        var availability = new DatabaseAvailability(new FixedClock(DateTimeOffset.UtcNow));

        availability.RecordFailure(new TimeoutException());
        availability.RecordFailure(new TimeoutException());
        availability.RecordSuccess();
        availability.RecordFailure(new TimeoutException());

        Assert.False(availability.IsOpen(out _));
    }

    [Fact]
    public async Task While_the_breaker_is_open_connections_fail_at_once_without_reaching_the_server()
    {
        var availability = new DatabaseAvailability(new FixedClock(DateTimeOffset.UtcNow));
        for (var failure = 0; failure < DatabaseAvailability.FailureThreshold; failure++)
        {
            availability.RecordFailure(new TimeoutException());
        }

        var interceptor = new DatabaseCircuitInterceptor(availability);
        await using var connection = new SqlConnection("Server=tcp:10.255.255.1,1433;Connect Timeout=30;Encrypt=True;");

        var error = await Assert.ThrowsAsync<DatabaseUnavailableException>(async () =>
            await interceptor.ConnectionOpeningAsync(connection, null!, default, Token));

        Assert.True(error.RetryAfter > TimeSpan.Zero);
    }

    [Theory]
    [MemberData(nameof(UnavailableExceptions))]
    public void Database_outages_are_recognized_also_when_wrapped(Exception exception, bool expected) =>
        Assert.Equal(expected, DatabaseUnavailableHandler.IsDatabaseUnavailable(exception, out _));

    public static TheoryData<Exception, bool> UnavailableExceptions() => new()
    {
        { new DatabaseUnavailableException("no responde", TimeSpan.FromSeconds(10)), true },
        { new RetryLimitExceededException("reintentos agotados", new TimeoutException()), true },
        { new CryptographicException("Data Protection no pudo leer sus claves", new TimeoutException()), true },
        { new InvalidOperationException("otro error"), false },
        { new DbUpdateException("restricción violada"), false },
    };

    [Fact]
    public async Task A_database_outage_shows_a_503_page_and_the_application_recovers()
    {
        var outage = new SimulatedOutage();
        await using var app = new FixTrackWebApplicationFactory();
        var production = app.WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("ReverseProxy:IgnoreForwardedHeaders", "true")
            .ConfigureTestServices(services => services.ConfigureDbContext<FixTrackDbContext>(options => options.AddInterceptors(outage))));
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await AuthenticatedClients.LogInAsync(client, TestAccounts.Operator.Email, TestAccounts.Operator.Password);

        outage.Active = true;
        using var unavailable = await client.GetAsync("/clientes", Token);
        var page = await unavailable.Content.ReadAsStringAsync(Token);
        outage.Active = false;
        using var recovered = await client.GetAsync("/clientes", Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.NotNull(unavailable.Headers.RetryAfter);
        Assert.Contains("Servicio temporalmente no disponible", page);
        Assert.Contains("no puede conectarse a la base de datos", page);
        Assert.DoesNotContain("Exception", page);
        Assert.DoesNotContain("at FixTrack", page);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Fact]
    public void The_database_access_has_bounded_timeouts_retries_and_a_breaker()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:FixTrack"] = "Server=tcp:no-existe.invalid,1433;Database=NoUsar;Connect Timeout=60;Encrypt=True;",
        }).Build();
        using var provider = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FixTrackDbContext>();

        var options = context.GetService<IDbContextOptions>();
        var interceptors = options.FindExtension<CoreOptionsExtension>()!.Interceptors ?? [];

        Assert.Equal(DependencyInjection.CommandTimeoutSeconds, context.Database.GetCommandTimeout());
        Assert.Equal(DependencyInjection.ConnectTimeoutSeconds, new SqlConnectionStringBuilder(context.Database.GetConnectionString()).ConnectTimeout);
        Assert.Contains(interceptors, interceptor => interceptor is DatabaseCircuitInterceptor);
        Assert.True(context.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void Each_request_is_cut_off_after_thirty_seconds_with_a_503()
    {
        var policy = factory.Services.GetRequiredService<IOptions<RequestTimeoutOptions>>().Value.DefaultPolicy!;

        Assert.Equal(TimeSpan.FromSeconds(30), policy.Timeout);
        Assert.Equal(503, policy.TimeoutStatusCode);
    }

    /// <summary>Hace que la base "deje de responder" mientras está activa.</summary>
    private sealed class SimulatedOutage : DbCommandInterceptor
    {
        public volatile bool Active;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            Active
                ? throw new DatabaseUnavailableException("Base simulada sin respuesta.", TimeSpan.FromSeconds(15))
                : ValueTask.FromResult(result);
    }
}
