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
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sql && UniqueViolationErrors.Contains(sql.Number))
        {
            throw new DuplicateValueException("El valor ya existe en la base de datos.", exception);
        }
    }
}
