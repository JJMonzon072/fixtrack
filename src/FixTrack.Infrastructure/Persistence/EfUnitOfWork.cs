using FixTrack.Application.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Persistence;

internal sealed class EfUnitOfWork(FixTrackDbContext db) : IUnitOfWork
{
    // Errores de SQL Server por clave duplicada en un índice único (2601) o restricción UNIQUE (2627).
    private static readonly int[] UniqueViolationErrors = [2601, 2627];

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("El registro fue modificado por otra solicitud.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sql && UniqueViolationErrors.Contains(sql.Number))
        {
            throw new DuplicateValueException("El valor ya existe en la base de datos.", exception);
        }
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
        where TResult : CommandResult
    {
        // Con reintentos ante fallos transitorios (EnableRetryOnFailure), una transacción iniciada por el usuario
        // debe ejecutarse dentro de la estrategia de ejecución, que repite la operación completa si es necesario.
        var strategy = db.Database.CreateExecutionStrategy();
        var attempt = 0;

        return strategy.ExecuteAsync(
            async token =>
            {
                if (attempt++ > 0)
                {
                    db.ChangeTracker.Clear();
                }

                // READ COMMITTED (el nivel predeterminado de SQL Server). Los bloqueos UPDLOCK que toman los
                // repositorios se mantienen hasta el final de esta transacción.
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var result = await operation(token);
                if (result.Succeeded)
                {
                    await transaction.CommitAsync(token);
                }
                else
                {
                    await transaction.RollbackAsync(token);
                    db.ChangeTracker.Clear();
                }

                return result;
            },
            cancellationToken);
    }
}
