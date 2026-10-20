using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FixTrack.Infrastructure.Persistence;

/// <summary>
/// Bloqueos de aplicación de SQL Server (<c>sp_getapplock</c>) para serializar operaciones que leen y luego escriben
/// sobre un conjunto de filas, como "siempre debe quedar un administrador habilitado". El bloqueo pertenece a la
/// transacción actual y se libera con su confirmación o reversión. Solo afecta a quienes piden el mismo recurso:
/// las consultas normales no esperan. Funciona en SQL Server Express y no depende de READ_COMMITTED_SNAPSHOT.
/// </summary>
internal static class ApplicationLocks
{
    /// <summary>Cambios de cuentas de acceso y roles.</summary>
    public const string Accounts = "FixTrack:cuentas";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Toma un bloqueo exclusivo sobre <paramref name="resource"/> hasta el final de la transacción actual. Devuelve
    /// <c>false</c> si no lo obtuvo dentro de <paramref name="timeout"/>. En SQLite (pruebas) no hace nada: ese motor
    /// ya admite una sola escritura a la vez.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si no hay una transacción activa.</exception>
    public static async Task<bool> TryAcquireExclusiveAsync(
        this DatabaseFacade database,
        string resource,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("El bloqueo de aplicación debe tomarse dentro de una transacción.");
        }

        if (!database.IsSqlServer())
        {
            return true;
        }

        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = @timeout",
            [result, new SqlParameter("@resource", resource), new SqlParameter("@timeout", (int)timeout.TotalMilliseconds)],
            cancellationToken);

        // 0 y 1: concedido (con o sin espera); negativo: tiempo agotado, cancelado o víctima de un interbloqueo.
        return (int)result.Value >= 0;
    }
}
