namespace FixTrack.Application.Common;

/// <summary>Confirma en la base de datos los cambios pendientes como una sola operación.</summary>
public interface IUnitOfWork
{
    /// <exception cref="DuplicateValueException">Si se viola una restricción de unicidad.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
