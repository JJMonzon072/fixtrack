using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FixTrack.Demo;

/// <summary>
/// Certificado de Data Protection de la demostración. FixTrack en producción exige uno para cifrar sus claves en la base;
/// este es autofirmado, se genera en cada entorno nuevo y vive solo en un volumen de Docker (nunca en el repositorio).
/// </summary>
internal static class DemoCertificate
{
    public const string Subject = "CN=FixTrack Demo Data Protection";

    /// <summary>Crea el certificado si no existe; si existe, comprueba que abra con la contraseña configurada.</summary>
    /// <returns><c>true</c> si se creó uno nuevo.</returns>
    public static bool Ensure(string path, string password)
    {
        if (File.Exists(path))
        {
            try
            {
                // Como en FixTrack: clave solo en memoria, salvo en macOS, que no admite claves efímeras.
                var flags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
                using var existing = X509CertificateLoader.LoadPkcs12FromFile(path, password, flags);
                return false;
            }
            catch (CryptographicException)
            {
                throw new DemoException(
                    $"El certificado {path} no abre con la contraseña configurada: el entorno se creó con otros valores. " +
                    "Recréalo desde cero (en Docker Compose: ./demo/demo.sh reiniciar).");
            }
        }

        File.WriteAllBytes(path, Create(password));
        if (!OperatingSystem.IsWindows())
        {
            // Solo la cuenta que ejecuta la demostración (la misma de la imagen de FixTrack) puede leerlo.
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return true;
    }

    /// <summary>PFX con una clave RSA de 3072 bits, cifrado con AES-256 y válido por dos años.</summary>
    public static byte[] Create(string password)
    {
        using var key = RSA.Create(3072);
        var request = new CertificateRequest(Subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, critical: true));
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(2));
        return certificate.ExportPkcs12(Pkcs12ExportPbeParameters.Pbes2Aes256Sha256, password);
    }
}
