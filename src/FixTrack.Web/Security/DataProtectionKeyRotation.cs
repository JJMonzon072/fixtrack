using FixTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FixTrack.Web.Security;

/// <summary>
/// Pasa el anillo de claves de Data Protection a claves cifradas con el certificado:
/// <list type="number">
/// <item>Crea una clave cifrada. Data Protection no la usa por defecto hasta que termina su periodo de propagación
/// (48 horas) mientras haya otra clave válida; hasta entonces, todo sigue funcionando con las claves anteriores.</item>
/// <item>Revoca las claves sin cifrar. Revocar agrega un registro: no borra filas. Desde ese momento esas claves no
/// descifran nada, así que leer la tabla ya no basta para falsificar una sesión, y la clave cifrada se usa de inmediato.
/// Lo emitido con las claves revocadas (sesiones abiertas, formularios a medio llenar) deja de ser válido.</item>
/// </list>
/// En un primer despliegue, sin sesiones reales, ambos pasos pueden hacerse a la vez. Con usuarios trabajando, conviene
/// revocar después de la propagación y de la vida de las sesiones (2 horas de inactividad).
/// <para>
/// Si hay claves vigentes que el certificado configurado no descifra (certificado equivocado, perdido o filtrado), no
/// hace nada salvo que se pida revocarlas: así un certificado equivocado no deja claves que después fallen. La instancia
/// en ejecución conserva su anillo de claves en memoria: después de revocar hay que reiniciarla.
/// </para>
/// </summary>
public sealed class DataProtectionKeyRotation(
    IKeyManager keyManager,
    DataProtectionKeysDbContext keys,
    IOptions<KeyManagementOptions> options,
    DataProtectionSetup.Settings settings,
    DataProtectionKeyCheck checker,
    TimeProvider clock)
{
    private const string EncryptedMarker = "<encryptedSecret";

    /// <summary>Estado de una clave, sin su material.</summary>
    /// <param name="IsReadable">¿Los certificados configurados descifran su material? Las claves sin cifrar siempre se leen.</param>
    public sealed record KeyStatus(
        Guid Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset ActivatesAt,
        DateTimeOffset ExpiresAt,
        bool IsEncrypted,
        bool IsRevoked,
        bool IsReadable);

    /// <param name="Configured">Falso si no hay certificado ni almacén en la base.</param>
    /// <param name="Problem">Motivo por el que no se hizo nada; <c>null</c> si la operación se completó.</param>
    public sealed record Result(bool Configured, string? Problem, Guid? CreatedKey, IReadOnlyList<Guid> RevokedKeys, IReadOnlyList<KeyStatus> Keys);

    public async Task<IReadOnlyList<KeyStatus>> ListAsync(CancellationToken cancellationToken)
    {
        var encrypted = await EncryptedKeyIdsAsync(cancellationToken);
        return [.. keyManager.GetAllKeys()
            .OrderBy(key => key.CreationDate)
            .Select(key => new KeyStatus(
                key.KeyId,
                key.CreationDate,
                key.ActivationDate,
                key.ExpirationDate,
                encrypted.Contains(key.KeyId),
                key.IsRevoked,
                DataProtectionKeyCheck.CanRead(key)))];
    }

    /// <param name="revokeUnencrypted">Revoca las claves guardadas sin cifrar (las sesiones emitidas con ellas terminan).</param>
    /// <param name="revokeUnreadable">
    /// Revoca las claves vigentes que el certificado configurado no descifra: para cuando el certificado anterior se perdió o
    /// se filtró. Sin esta opción, si existen, no se hace nada.
    /// </param>
    public async Task<Result> ProtectAsync(bool revokeUnencrypted, CancellationToken cancellationToken, bool revokeUnreadable = false)
    {
        if (settings.Store != DataProtectionSetup.KeyStore.Database || !settings.ProtectsKeys)
        {
            return new Result(false, "Configura DataProtection:Certificate (y DataProtection:KeyStore=Database) antes de proteger las claves.", null, [], []);
        }

        var now = clock.GetUtcNow();
        var keys = await ListAsync(cancellationToken);
        var unreadable = keys.Where(key => !key.IsRevoked && key.ExpiresAt > now && !key.IsReadable).Select(key => key.Id).ToList();
        if (unreadable.Count > 0 && !revokeUnreadable)
        {
            return new Result(true, checker.UnreadableMessage(unreadable), null, [], keys);
        }

        var revoked = new List<Guid>();
        foreach (var id in unreadable)
        {
            keyManager.RevokeKey(id, "El certificado que la cifró no está disponible (perdido o reemplazado).");
            revoked.Add(id);
        }

        Guid? created = null;
        if (!keys.Any(key => key.IsEncrypted && key.IsReadable && !key.IsRevoked && key.ActivatesAt <= now && key.ExpiresAt > now))
        {
            // Activa desde ahora: si no queda otra clave válida (tras revocar), se usa de inmediato.
            created = keyManager.CreateNewKey(now, now + options.Value.NewKeyLifetime).KeyId;
        }

        if (revokeUnencrypted)
        {
            foreach (var key in keys.Where(key => !key.IsEncrypted && !key.IsRevoked))
            {
                keyManager.RevokeKey(key.Id, "Clave guardada sin cifrar, reemplazada por una cifrada con certificado.");
                revoked.Add(key.Id);
            }
        }

        return new Result(true, null, created, revoked, await ListAsync(cancellationToken));
    }

    /// <summary>Claves cuyo XML guardado tiene el material cifrado. El nombre de la fila es "key-{guid}".</summary>
    private async Task<HashSet<Guid>> EncryptedKeyIdsAsync(CancellationToken cancellationToken)
    {
        var rows = await keys.DataProtectionKeys.AsNoTracking()
            .Where(row => row.FriendlyName != null && row.FriendlyName.StartsWith("key-"))
            .Select(row => new { row.FriendlyName, row.Xml })
            .ToListAsync(cancellationToken);

        return [.. rows
            .Where(row => row.Xml is not null && row.Xml.Contains(EncryptedMarker, StringComparison.Ordinal))
            .Select(row => Guid.TryParse(row.FriendlyName!["key-".Length..], out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)];
    }
}
