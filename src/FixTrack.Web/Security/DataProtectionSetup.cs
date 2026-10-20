using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FixTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace FixTrack.Web.Security;

/// <summary>
/// Claves de ASP.NET Core Data Protection, que protegen la cookie de sesión, los tokens antifalsificación y TempData.
/// <list type="bullet">
/// <item><c>DataProtection:KeyStore</c>: <c>Database</c> (tabla DataProtectionKeys; predeterminado fuera de Desarrollo,
/// para que las sesiones sobrevivan a los reinicios del contenedor) o <c>Local</c> (carpeta del perfil del usuario;
/// predeterminado en Desarrollo, para que un equipo de desarrollo nunca escriba claves en la base compartida).</item>
/// <item><c>DataProtection:Certificate</c>: certificado X.509 con clave privada RSA que cifra las claves en reposo. Se da
/// como PFX en base64 (<c>Base64</c>) o como ruta a un archivo (<c>Path</c>), con su contraseña (<c>Password</c>).</item>
/// <item><c>DataProtection:PreviousCertificates:N</c>: certificados anteriores, solo para descifrar durante una rotación.</item>
/// <item><c>DataProtection:AllowUnprotectedKeys</c>: fuera de Desarrollo, guardar claves en la base sin certificado
/// exige declararlo con este valor. Sin certificado ni esta declaración, la aplicación no arranca.</item>
/// </list>
/// Las claves que ya estaban sin cifrar se siguen leyendo; las nuevas se cifran. El comando <c>proteger-claves</c> crea
/// una clave cifrada de inmediato y, más adelante, revoca las anteriores (ver el README).
/// </summary>
public static class DataProtectionSetup
{
    public const string Section = "DataProtection";
    public const string ApplicationName = "FixTrack";

    public enum KeyStore
    {
        Database,
        Local,
    }

    /// <summary>Configuración leída y validada.</summary>
    public sealed record Settings(KeyStore Store, X509Certificate2? Certificate, IReadOnlyList<X509Certificate2> PreviousCertificates, bool AllowUnprotectedKeys)
    {
        public bool ProtectsKeys => Certificate is not null;
    }

    /// <exception cref="InvalidOperationException">Si la configuración es inválida o insegura para el entorno.</exception>
    public static Settings Read(IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(Section);
        var store = section["KeyStore"] is { Length: > 0 } configured
            ? Enum.TryParse<KeyStore>(configured, ignoreCase: true, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"DataProtection:KeyStore debe ser Database o Local; se recibió «{configured}».")
            : environment.IsDevelopment() ? KeyStore.Local : KeyStore.Database;
        var certificate = LoadCertificate(section.GetSection("Certificate"), "DataProtection:Certificate");
        var previous = section.GetSection("PreviousCertificates").GetChildren()
            .Select((child, index) => LoadCertificate(child, $"DataProtection:PreviousCertificates:{index}"))
            .OfType<X509Certificate2>()
            .ToList();
        var allowUnprotected = section.GetValue("AllowUnprotectedKeys", false);

        if (store == KeyStore.Database && certificate is null && !allowUnprotected && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Las claves de Data Protection se guardan en la base y deben cifrarse con un certificado: define " +
                "DataProtection__Certificate__Base64 (PFX en base64) y DataProtection__Certificate__Password. Si de verdad " +
                "quieres guardarlas sin cifrar, decláralo con DataProtection__AllowUnprotectedKeys=true. Consulta el README.");
        }

        return new Settings(store, certificate, previous, allowUnprotected);
    }

    public static IServiceCollection AddFixTrackDataProtection(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var settings = Read(configuration, environment);
        services.AddSingleton(settings);
        services.AddScoped<DataProtectionKeyRotation>();

        var builder = services.AddDataProtection().SetApplicationName(ApplicationName);
        if (settings.Store == KeyStore.Database)
        {
            builder.PersistKeysToDbContext<DataProtectionKeysDbContext>();
        }

        if (settings.Certificate is { } certificate)
        {
            // Cifra las claves nuevas y también registra el certificado para descifrarlas (no hace falta el almacén X509).
            builder.ProtectKeysWithCertificate(certificate);
        }

        if (settings.PreviousCertificates.Count > 0)
        {
            builder.UnprotectKeysWithAnyCertificate([.. settings.PreviousCertificates]);
        }

        return services;
    }

    private static X509Certificate2? LoadCertificate(IConfigurationSection section, string name)
    {
        var base64 = section["Base64"];
        var path = section["Path"];
        var password = section["Password"];
        if (string.IsNullOrWhiteSpace(base64) && string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(base64) && !string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"{name}: indica Base64 o Path, no ambos.");
        }

        // En Linux y Windows la clave privada queda solo en memoria; macOS no admite claves efímeras.
        var flags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
        X509Certificate2 certificate;
        try
        {
            certificate = string.IsNullOrWhiteSpace(path)
                ? X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(base64!.Trim()), password, flags)
                : X509CertificateLoader.LoadPkcs12FromFile(path, password, flags);
        }
        catch (Exception error) when (error is CryptographicException or FormatException or IOException or UnauthorizedAccessException)
        {
            // El mensaje no incluye la contraseña ni el contenido del certificado.
            throw new InvalidOperationException($"{name}: no se pudo cargar el certificado (PFX y contraseña). {error.GetType().Name}.", error);
        }

        using var key = certificate.GetRSAPrivateKey();
        if (key is null)
        {
            throw new InvalidOperationException($"{name}: el certificado debe incluir una clave privada RSA.");
        }

        return certificate;
    }
}
