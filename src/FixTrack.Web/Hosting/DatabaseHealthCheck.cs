using FixTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FixTrack.Web.Hosting;

/// <summary>
/// Comprueba que la base de datos responde a una consulta (no basta con abrir la conexión). El resultado público no
/// incluye detalles del error. Se reutiliza durante unos segundos (<see cref="DatabaseHealthCache"/>): /salud/bd es
/// pública y, sin ese límite, cualquiera podría multiplicar las conexiones a la base.
/// </summary>
public sealed class DatabaseHealthCheck(FixTrackDbContext db, DatabaseHealthCache cache) : IHealthCheck
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        cache.GetAsync(QueryAsync, cancellationToken);

    private async Task<HealthCheckResult> QueryAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(exception: exception);
        }
    }
}

/// <summary>
/// Último resultado de la comprobación de la base, compartido por todas las solicitudes durante <see cref="Lifetime"/>.
/// Si varias llegan a la vez sin resultado vigente, solo una consulta la base y las demás esperan su resultado.
/// </summary>
public sealed class DatabaseHealthCache(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Entry? _last;

    public async Task<HealthCheckResult> GetAsync(Func<CancellationToken, Task<HealthCheckResult>> check, CancellationToken cancellationToken)
    {
        if (Current() is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Current() is { } fresh)
            {
                return fresh;
            }

            var result = await check(cancellationToken);
            Volatile.Write(ref _last, new Entry(result, clock.GetUtcNow() + Lifetime));
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private HealthCheckResult? Current() =>
        Volatile.Read(ref _last) is { } last && clock.GetUtcNow() < last.ExpiresAt ? last.Result : null;

    private sealed record Entry(HealthCheckResult Result, DateTimeOffset ExpiresAt);
}
