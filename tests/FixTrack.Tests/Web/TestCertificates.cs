using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;

namespace FixTrack.Tests.Web;

/// <summary>
/// Certificados autofirmados para Data Protection, generados al azar en cada ejecución: no hay ninguna clave privada ni
/// contraseña en el repositorio.
/// </summary>
public static class TestCertificates
{
    private static readonly Lazy<(string Base64, string Password)> Shared = new(() => Create("FixTrack pruebas"));

    /// <summary>PFX en base64 y su contraseña.</summary>
    public static (string Base64, string Password) Create(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return (Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, password)), password);
    }

    /// <summary>Claves en la base de la prueba, cifradas con el certificado compartido.</summary>
    public static IWebHostBuilder UseProtectedKeysInDatabase(this IWebHostBuilder builder, (string Base64, string Password)? certificate = null)
    {
        var (base64, password) = certificate ?? Shared.Value;
        return builder
            .UseSetting("DataProtection:KeyStore", "Database")
            .UseSetting("DataProtection:Certificate:Base64", base64)
            .UseSetting("DataProtection:Certificate:Password", password);
    }
}
