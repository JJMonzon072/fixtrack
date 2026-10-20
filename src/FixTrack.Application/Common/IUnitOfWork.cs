namespace FixTrack.Application.Common;

/// <summary>Confirma en la base de datos los cambios pendientes como una sola operación.</summary>
public interface IUnitOfWork
{
    /// <exception cref="DuplicateValueException">Si se viola una restricción de unicidad.</exception>
    /// <exception cref="ConcurrencyConflictException">Si otra solicitud modificó el registro después de leerlo.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ejecuta la operación dentro de una transacción de base de datos. La transacción se confirma si el
    /// resultado es exitoso y se revierte si no lo es o si ocurre una excepción. Ante un fallo transitorio
    /// de conexión, la operación completa se repite desde el inicio: debe leer dentro de ella todo lo que modifica.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
        where TResult : CommandResult;
}
