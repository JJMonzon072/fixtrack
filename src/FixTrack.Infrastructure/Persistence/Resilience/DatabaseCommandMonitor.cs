using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FixTrack.Infrastructure.Persistence.Resilience;

/// <summary>
/// Informa a <see cref="DatabaseAvailability"/> lo que pasa con los comandos. Un servidor que acepta la conexión pero no
/// responde (como Somee cuando se congela) no produce fallos de conexión: los comandos agotan su tiempo sobre conexiones
/// ya abiertas. Esos tiempos agotados cuentan como fallos, para que el disyuntor se abra y las solicitudes siguientes
/// reciban la página 503 de inmediato en lugar de esperar cada una. Un comando que termina bien demuestra que la base
/// responde y restablece el disyuntor.
/// </summary>
public sealed class DatabaseCommandMonitor(DatabaseAvailability availability) : DbCommandInterceptor
{
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => availability.RecordFailure(eventData.Exception);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        availability.RecordFailure(eventData.Exception);
        return Task.CompletedTask;
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result) => Succeeded(result);

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(Succeeded(result));

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result) => Succeeded(result);

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(Succeeded(result));

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result) => Succeeded(result);

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(Succeeded(result));

    private T Succeeded<T>(T result)
    {
        availability.RecordSuccess();
        return result;
    }
}
