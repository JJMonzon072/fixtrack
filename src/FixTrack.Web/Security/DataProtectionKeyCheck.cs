using System.Security.Cryptography;
using FixTrack.Web.Hosting;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace FixTrack.Web.Security;

/// <summary>
/// Comprueba, antes de iniciar el servidor, que los certificados configurados descifran las claves vigentes de la base.
/// <para>
/// Con un certificado equivocado, Data Protection no se detiene: registra errores, invalida todas las sesiones y crea una
/// clave nueva cifrada con ese certificado, que sigue fallando aunque después se corrija la configuración. Esta
/// comprobación se ejecuta antes de que Data Protection cargue su anillo de claves: la aplicación no arranca, no escribe
/// nada y el despliegue falla, de modo que la plataforma conserva la versión anterior.
/// </para>
/// <para>
/// Si la base no responde, no bloquea el arranque (la aplicación muestra la página 503 hasta que vuelva). También avisa
/// en la bitácora cuando el certificado vence pronto: Data Protection no valida su vigencia, pero conviene rotarlo.
/// </para>
/// </summary>
public sealed class DataProtectionKeyCheck(
    IKeyManager keyManager,
    DataProtectionSetup.Settings settings,
    TimeProvider clock,
    ILogger<DataProtectionKeyCheck> logger)
{
    /// <summary>Antelación con la que se avisa del vencimiento del certificado.</summary>
    public static readonly TimeSpan ExpirationNotice = TimeSpan.FromDays(90);

    public const string RevokeUnreadableOption = "--revocar-ilegibles";

    /// <summary>Lanza <see cref="InvalidOperationException"/> si alguna clave vigente no se puede descifrar.</summary>
    public void EnsureReadable()
    {
        if (settings.Store != DataProtectionSetup.KeyStore.Database)
        {
            return;
        }

        ReportExpiration();
        IReadOnlyList<Guid> unreadable;
        try
        {
            unreadable = UnreadableKeys();
        }
        catch (Exception error) when (DatabaseUnavailableHandler.IsDatabaseUnavailable(error, out _))
        {
            logger.LogWarning("No se pudo comprobar el certificado de Data Protection: la base de datos no respondió. Se comprobará en el próximo inicio.");
            return;
        }

        if (unreadable.Count > 0)
        {
            throw new InvalidOperationException(UnreadableMessage(unreadable));
        }
    }

    /// <summary>Claves vigentes (ni revocadas ni vencidas) que los certificados configurados no descifran.</summary>
    public IReadOnlyList<Guid> UnreadableKeys()
    {
        var now = clock.GetUtcNow();
        return [.. keyManager.GetAllKeys()
            .Where(key => !key.IsRevoked && key.ExpirationDate > now)
            .Where(key => !CanRead(key))
            .OrderBy(key => key.CreationDate)
            .Select(key => key.KeyId)];
    }

    /// <summary>¿Se puede descifrar el material de la clave? Las claves sin cifrar siempre se pueden leer.</summary>
    public static bool CanRead(IKey key)
    {
        try
        {
            _ = key.CreateEncryptor();
            return true;
        }
        catch (Exception error) when (!DatabaseUnavailableHandler.IsDatabaseUnavailable(error, out _))
        {
            return false;
        }
    }

    public string UnreadableMessage(IReadOnlyCollection<Guid> keys) =>
        $"Data Protection: el certificado configurado{Thumbprint()} no puede descifrar {keys.Count} clave(s) vigente(s) de la tabla " +
        $"DataProtectionKeys ({string.Join(", ", keys)}). No se inició la aplicación para no invalidar las sesiones ni crear claves " +
        "que después fallen. Si el certificado no es el correcto, configura el que cifró esas claves (DataProtection__Certificate__*); " +
        "durante una rotación, agrega el anterior como DataProtection__PreviousCertificates__0__*. Si ese certificado se perdió o se " +
        $"filtró, ejecuta proteger-claves {RevokeUnreadableOption} con el certificado nuevo: las sesiones abiertas tendrán que iniciarse de nuevo.";

    private string Thumbprint() =>
        settings.Certificate is { } certificate ? $" (huella SHA-256 {certificate.GetCertHashString(HashAlgorithmName.SHA256)})" : " (ninguno)";

    private void ReportExpiration()
    {
        if (settings.Certificate is not { } certificate)
        {
            return;
        }

        var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        var now = clock.GetUtcNow();
        if (notAfter <= now)
        {
            logger.LogWarning(
                "El certificado de Data Protection venció el {NotAfter:yyyy-MM-dd}. Sigue cifrando y descifrando las claves (Data Protection no valida la vigencia), pero conviene rotarlo: consulta docs/checklist-lanzamiento.md.",
                notAfter);
        }
        else if (notAfter - now <= ExpirationNotice)
        {
            logger.LogWarning(
                "El certificado de Data Protection vence el {NotAfter:yyyy-MM-dd}: prepara su rotación (docs/checklist-lanzamiento.md).",
                notAfter);
        }
        else
        {
            logger.LogInformation("Certificado de Data Protection vigente hasta el {NotAfter:yyyy-MM-dd}.", notAfter);
        }
    }
}
