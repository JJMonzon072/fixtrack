using System.Security.Cryptography;
using FixTrack.Infrastructure;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Web.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FixTrack.Tests.Web;

/// <summary>Claves de Data Protection cifradas con un certificado, compatibilidad con las anteriores y rotación.</summary>
public sealed partial class DataProtectionTests
{
    private const string Purpose = "FixTrack.Pruebas";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static IDataProtector Protector(WebApplicationFactory<Program> app) =>
        app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

    private static async Task<List<DataProtectionKey>> StoredKeysAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DataProtectionKeysDbContext>().DataProtectionKeys.AsNoTracking()
            .Where(row => row.FriendlyName!.StartsWith("key-"))
            .ToListAsync(Token);
    }

    private static async Task<DataProtectionKeyRotation.Result> ProtectAsync(WebApplicationFactory<Program> app, bool revoke)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DataProtectionKeyRotation>().ProtectAsync(revoke, Token);
    }

    /// <summary>Una instancia de la aplicación sobre la misma base, sin certificado (como antes de esta fase).</summary>
    private static WebApplicationFactory<Program> WithoutCertificate(FixTrackWebApplicationFactory database) =>
        database.WithWebHostBuilder(builder => builder
            .UseSetting("DataProtection:Certificate:Base64", string.Empty)
            .UseSetting("DataProtection:Certificate:Password", string.Empty));

    [Fact]
    public async Task New_keys_are_stored_encrypted_with_the_certificate()
    {
        await using var app = new FixTrackWebApplicationFactory();

        Protector(app).Protect("dato");
        var keys = await StoredKeysAsync(app);

        Assert.NotEmpty(keys);
        Assert.All(keys, key =>
        {
            Assert.Contains("<encryptedSecret", key.Xml);
            Assert.DoesNotContain("<masterKey", key.Xml);
        });
    }

    [Fact]
    public async Task Old_unencrypted_keys_keep_working_until_the_rotation_revokes_them()
    {
        await using var database = new FixTrackWebApplicationFactory();

        // 1. Antes: claves sin cifrar (como en la base actual).
        await using var before = WithoutCertificate(database);
        var oldPayload = Protector(before).Protect("sesión anterior");
        var oldKey = KeyOf(oldPayload);
        Assert.Contains(await StoredKeysAsync(before), key => key.Xml!.Contains("<masterKey", StringComparison.Ordinal));

        // 2. Con el certificado, lo protegido antes se sigue leyendo. proteger-claves crea una clave cifrada, pero
        // Data Protection no la usa por defecto hasta que termina su periodo de propagación mientras haya otra válida.
        await using var after = database.WithWebHostBuilder(_ => { });
        Assert.Equal("sesión anterior", Protector(after).Unprotect(oldPayload));
        var rotation = await ProtectAsync(after, revoke: false);
        Assert.NotNull(rotation.CreatedKey);
        Assert.Contains(rotation.Keys, key => key.Id == rotation.CreatedKey && key.IsEncrypted);
        Assert.Equal(oldKey, KeyOf(Protector(after).Protect("sesión durante la propagación")));
        Assert.Null((await ProtectAsync(after, revoke: false)).CreatedKey);

        // 3. Al revocar las claves sin cifrar, lo emitido con ellas deja de leerse y la clave cifrada se usa de inmediato.
        var revocation = await ProtectAsync(after, revoke: true);
        await using var later = database.WithWebHostBuilder(_ => { });
        var newPayload = Protector(later).Protect("sesión nueva");
        Assert.Equal([oldKey], revocation.RevokedKeys);
        Assert.ThrowsAny<CryptographicException>(() => Protector(later).Unprotect(oldPayload));
        Assert.Equal(rotation.CreatedKey, KeyOf(newPayload));
        Assert.Equal("sesión nueva", Protector(later).Unprotect(newPayload));
        Assert.All(revocation.Keys.Where(key => !key.IsEncrypted), key => Assert.True(key.IsRevoked));
    }

    [Fact]
    public async Task Revoking_on_a_first_deployment_switches_to_an_encrypted_key_at_once()
    {
        await using var database = new FixTrackWebApplicationFactory();
        await using (var before = WithoutCertificate(database))
        {
            Protector(before).Protect("clave sin cifrar existente");
        }

        // Un solo paso: crear la clave cifrada y revocar las anteriores.
        await using var deploy = database.WithWebHostBuilder(_ => { });
        var result = await ProtectAsync(deploy, revoke: true);
        await using var running = database.WithWebHostBuilder(_ => { });

        Assert.NotNull(result.CreatedKey);
        Assert.Equal(result.CreatedKey, KeyOf(Protector(running).Protect("dato")));
    }

    /// <summary>Id de la clave que protegió el dato: va en los bytes 4 a 19 del formato de Data Protection.</summary>
    private static Guid KeyOf(string payload) => new(WebEncoders.Base64UrlDecode(payload).AsSpan(4, 16));

    [Fact]
    public async Task The_rotation_needs_a_certificate()
    {
        await using var database = new FixTrackWebApplicationFactory();
        await using var app = WithoutCertificate(database);

        var result = await ProtectAsync(app, revoke: true);

        Assert.False(result.Configured);
        Assert.Contains("DataProtection:Certificate", result.Problem);
    }

    [Fact]
    public void Production_requires_a_certificate_or_an_explicit_declaration()
    {
        using var production = new FixTrackWebApplicationFactory().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("ReverseProxy:IgnoreForwardedHeaders", "true")
            .UseSetting("DataProtection:Certificate:Base64", string.Empty));

        var error = Assert.ThrowsAny<Exception>(() => production.CreateClient());

        Assert.Contains("DataProtection__Certificate__Base64", Messages(error));
    }

    [Fact]
    public void Production_can_explicitly_accept_unencrypted_keys()
    {
        using var production = new FixTrackWebApplicationFactory().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("ReverseProxy:IgnoreForwardedHeaders", "true")
            .UseSetting("DataProtection:Certificate:Base64", string.Empty)
            .UseSetting("DataProtection:AllowUnprotectedKeys", "true"));

        using var client = production.CreateClient();

        Assert.False(production.Services.GetRequiredService<DataProtectionSetup.Settings>().ProtectsKeys);
    }

    [Theory]
    [InlineData("no-es-base64", null)]
    [InlineData(null, "contraseña-incorrecta")]
    public void An_invalid_certificate_stops_the_application_without_revealing_secrets(string? base64, string? password)
    {
        var valid = TestCertificates.Create("FixTrack certificado inválido");
        using var app = new FixTrackWebApplicationFactory().WithWebHostBuilder(builder => builder
            .UseSetting("DataProtection:Certificate:Base64", base64 ?? valid.Base64)
            .UseSetting("DataProtection:Certificate:Password", password ?? valid.Password));

        var message = Messages(Assert.ThrowsAny<Exception>(() => app.CreateClient()));

        Assert.Contains("no se pudo cargar el certificado", message);
        Assert.DoesNotContain(valid.Password, message);
        Assert.DoesNotContain("contraseña-incorrecta", message);
    }

    [Fact]
    public void Development_keeps_its_keys_out_of_the_shared_database_by_default()
    {
        var configuration = new ConfigurationBuilder().Build();

        var development = DataProtectionSetup.Read(configuration, new TestEnvironment("Development"));
        var production = DataProtectionSetup.Read(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:AllowUnprotectedKeys"] = "true" }).Build(),
            new TestEnvironment("Production"));

        Assert.Equal(DataProtectionSetup.KeyStore.Local, development.Store);
        Assert.Equal(DataProtectionSetup.KeyStore.Database, production.Store);
    }

    [Fact]
    public void The_key_store_fails_fast_when_the_database_does_not_answer()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:FixTrack"] = "Server=tcp:no-existe.invalid,1433;Database=NoUsar;Connect Timeout=30;Encrypt=True;",
        }).Build();
        using var provider = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
        using var scope = provider.CreateScope();

        var keys = scope.ServiceProvider.GetRequiredService<DataProtectionKeysDbContext>().Database;

        Assert.Equal(DependencyInjection.DataProtectionKeysTimeoutSeconds, keys.GetCommandTimeout());
        Assert.Equal(DependencyInjection.DataProtectionKeysTimeoutSeconds, new SqlConnectionStringBuilder(keys.GetConnectionString()).ConnectTimeout);
    }

    [Fact]
    public async Task The_application_reads_its_keys_through_the_dedicated_context()
    {
        await using var app = new FixTrackWebApplicationFactory();

        var repository = app.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;

        Assert.IsType<EntityFrameworkCoreXmlRepository<DataProtectionKeysDbContext>>(repository);
    }

    private static string Messages(Exception error)
    {
        var messages = new List<string>();
        for (var current = error; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private sealed class TestEnvironment(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "FixTrack.Web";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
