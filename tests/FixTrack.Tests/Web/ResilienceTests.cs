using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using FixTrack.Application.Common;
using FixTrack.Infrastructure;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Infrastructure.Persistence.Resilience;
using FixTrack.Tests.Application;
using FixTrack.Web.Hosting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
using Microsoft.Extensions.Logging;
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
    public void An_outage_is_logged_with_its_detail_once_and_the_recovery_in_one_line()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var logger = new CapturingLogger<DatabaseAvailability>();
        var availability = new DatabaseAvailability(clock, logger);

        // Tres aperturas seguidas del disyuntor durante la misma caída.
        for (var opening = 0; opening < 3; opening++)
        {
            for (var failure = 0; failure < DatabaseAvailability.FailureThreshold; failure++)
            {
                availability.RecordFailure(new TimeoutException("sin respuesta"));
            }

            clock.Advance(DatabaseAvailability.BreakDuration);
        }

        availability.RecordSuccess();
        availability.RecordSuccess();

        Assert.Collection(
            logger.Entries,
            warning =>
            {
                Assert.Equal(LogLevel.Warning, warning.Level);
                Assert.IsType<TimeoutException>(warning.Exception);
            },
            recovered =>
            {
                Assert.Equal(LogLevel.Information, recovered.Level);
                Assert.Null(recovered.Exception);
                Assert.Contains("volvió a responder después de 45 s", recovered.Message);
            });
    }

    [Theory]
    [MemberData(nameof(Causes))]
    public async Task Each_rejected_request_logs_one_short_line_without_the_stack_trace(Exception exception, string cause)
    {
        var logger = new CapturingLogger<DatabaseUnavailableHandler>();
        var context = new DefaultHttpContext { Response = { Body = Stream.Null } };
        context.Request.Method = "GET";
        context.Request.Path = "/error";
        context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature { Path = "/clientes", Error = exception });

        var handled = await new DatabaseUnavailableHandler(logger).TryHandleAsync(context, exception, Token);

        Assert.True(handled);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal($"Base de datos no disponible ({cause}): se respondió 503 a GET /clientes.", entry.Message);
    }

    public static TheoryData<Exception, string> Causes() => new()
    {
        { new DatabaseUnavailableException("abierto", TimeSpan.FromSeconds(10)), "disyuntor abierto" },
        { new RetryLimitExceededException("agotados", new TimeoutException()), "TimeoutException" },
        { new CryptographicException("claves", new InvalidOperationException("envoltorio", new TimeoutException())), "TimeoutException" },
    };

    [Fact]
    public void Data_protection_does_not_repeat_its_key_ring_error_on_every_request()
    {
        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        Assert.Equal("Critical", configuration["Logging:LogLevel:Microsoft.AspNetCore.DataProtection.KeyManagement.KeyRingProvider"]);
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

    [Fact]
    public async Task A_server_that_accepts_connections_but_does_not_answer_opens_the_breaker()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var availability = new DatabaseAvailability(clock);
        var frozen = new FrozenServer { Active = true };

        // Sin una conexión abierta de antemano: EF abre y cierra en cada operación, como con SQL Server y su pool.
        await using var context = new FixTrackDbContext(new DbContextOptionsBuilder<FixTrackDbContext>()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(frozen, new DatabaseCommandMonitor(availability), new DatabaseCircuitInterceptor(availability))
            .Options);

        for (var attempt = 0; attempt < DatabaseAvailability.FailureThreshold; attempt++)
        {
            // Abrir una conexión (del pool, sin llegar al servidor) no prueba que la base responda.
            await context.Database.OpenConnectionAsync(Token);
            await context.Database.CloseConnectionAsync();
            await Assert.ThrowsAsync<TimeoutException>(() => context.Database.ExecuteSqlRawAsync("SELECT 1", Token));
        }

        var blocked = await Assert.ThrowsAsync<DatabaseUnavailableException>(() => context.Database.ExecuteSqlRawAsync("SELECT 1", Token));
        var commandsWhileOpen = frozen.Commands;

        frozen.Active = false;
        clock.Advance(DatabaseAvailability.BreakDuration);
        await context.Database.ExecuteSqlRawAsync("SELECT 1", Token);

        // Un comando correcto restablece el conteo: dos fallos nuevos no abren el disyuntor.
        frozen.Active = true;
        await Assert.ThrowsAsync<TimeoutException>(() => context.Database.ExecuteSqlRawAsync("SELECT 1", Token));
        await Assert.ThrowsAsync<TimeoutException>(() => context.Database.ExecuteSqlRawAsync("SELECT 1", Token));

        Assert.True(blocked.RetryAfter > TimeSpan.Zero);
        Assert.Equal(DatabaseAvailability.FailureThreshold, commandsWhileOpen);
        Assert.False(availability.IsOpen(out _));
    }

    [Fact]
    public async Task The_503_page_is_sent_even_if_the_request_was_already_cut_by_the_time_limit()
    {
        var body = new MemoryStream();
        var context = new DefaultHttpContext { Response = { Body = body } };
        context.Request.Method = "GET";
        context.Request.Path = "/ordenes";

        var handled = await new DatabaseUnavailableHandler(new CapturingLogger<DatabaseUnavailableHandler>())
            .TryHandleAsync(context, new DatabaseUnavailableException("sin respuesta", TimeSpan.FromSeconds(15)), new CancellationToken(canceled: true));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Contains("Servicio temporalmente no disponible", System.Text.Encoding.UTF8.GetString(body.ToArray()));
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
        Assert.Contains(interceptors, interceptor => interceptor is DatabaseCommandMonitor);
        Assert.True(context.Database.CreateExecutionStrategy().RetriesOnFailure);

        // Una caída no repite la traza en cada conexión, consulta o guardado fallido (ver QuietExpectedFailures).
        var warnings = options.FindExtension<CoreOptionsExtension>()!.WarningsConfiguration;
        Assert.Equal(LogLevel.Debug, warnings.GetLevel(RelationalEventId.ConnectionError));
        Assert.Equal(LogLevel.Debug, warnings.GetLevel(CoreEventId.QueryIterationFailed));
        Assert.Equal(LogLevel.Debug, warnings.GetLevel(CoreEventId.SaveChangesFailed));
        Assert.Null(warnings.GetLevel(RelationalEventId.CommandError));
    }

    [Fact]
    public void Each_request_is_cut_off_after_thirty_seconds_with_a_503()
    {
        var policy = factory.Services.GetRequiredService<IOptions<RequestTimeoutOptions>>().Value.DefaultPolicy!;

        Assert.Equal(TimeSpan.FromSeconds(30), policy.Timeout);
        Assert.Equal(503, policy.TimeoutStatusCode);
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    /// <summary>Un servidor que acepta la conexión pero no responde: cada comando agota su tiempo.</summary>
    private sealed class FrozenServer : DbCommandInterceptor
    {
        private int _commands;

        public volatile bool Active;

        public int Commands => _commands;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commands);
            return Active ? throw new TimeoutException("El servidor no respondió (simulado).") : ValueTask.FromResult(result);
        }
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
