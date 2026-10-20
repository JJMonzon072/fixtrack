using System.Data.Common;
using FixTrack.Application.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FixTrack.Infrastructure.Persistence.Resilience;

/// <summary>Aplica <see cref="DatabaseAvailability"/> a cada apertura de conexión de EF Core.</summary>
public sealed class DatabaseCircuitInterceptor(DatabaseAvailability availability) : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ThrowIfOpen();
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfOpen();
        return ValueTask.FromResult(result);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => availability.RecordSuccess();

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        availability.RecordSuccess();
        return Task.CompletedTask;
    }

    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) =>
        availability.RecordFailure(eventData.Exception);

    public override Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        availability.RecordFailure(eventData.Exception);
        return Task.CompletedTask;
    }

    private void ThrowIfOpen()
    {
        if (availability.IsOpen(out var retryAfter))
        {
            throw new DatabaseUnavailableException(
                "La base de datos no respondió en los últimos intentos; se volverá a probar en unos segundos.", retryAfter);
        }
    }
}
