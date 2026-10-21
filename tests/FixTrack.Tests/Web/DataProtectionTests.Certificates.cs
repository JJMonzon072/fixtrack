using System.Collections.Concurrent;
using System.Xml.Linq;
using FixTrack.Web.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FixTrack.Tests.Web;

/// <summary>Certificado equivocado, rotación, certificado perdido y vencimiento.</summary>
public sealed partial class DataProtectionTests
{
    /// <summary>Otra instancia sobre la misma base con un certificado dado y, si se indica, el anterior para descifrar.</summary>
    private static WebApplicationFactory<Program> WithCertificate(
        FixTrackWebApplicationFactory database,
        (string Base64, string Password) certificate,
        (string Base64, string Password)? previous = null,
        ILoggerProvider? logs = null) =>
        database.WithWebHostBuilder(builder =>
        {
            builder.UseProtectedKeysInDatabase(certificate);
            if (previous is { } old)
            {
                builder
                    .UseSetting("DataProtection:PreviousCertificates:0:Base64", old.Base64)
                    .UseSetting("DataProtection:PreviousCertificates:0:Password", old.Password);
            }

            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
            }
        });

    [Fact]
    public async Task A_certificate_that_cannot_read_the_stored_keys_stops_the_application_and_creates_no_key()
    {
        await using var database = new FixTrackWebApplicationFactory();
        await using var original = WithCertificate(database, TestCertificates.Create("FixTrack certificado original"));
        var payload = Protector(original).Protect("sesión");
        var stored = await StoredKeysAsync(original);

        await using var wrong = WithCertificate(database, TestCertificates.Create("FixTrack certificado equivocado"));
        var message = Messages(Assert.ThrowsAny<Exception>(() => wrong.CreateClient()));

        Assert.Contains("no puede descifrar 1 clave(s) vigente(s)", message);
        Assert.Contains(stored.Single().FriendlyName!["key-".Length..], message);
        Assert.Contains(DataProtectionKeyCheck.RevokeUnreadableOption, message);
        Assert.Equal(stored.Count, (await StoredKeysAsync(original)).Count);
        Assert.Equal("sesión", Protector(original).Unprotect(payload));
    }

    [Fact]
    public async Task During_a_rotation_the_previous_certificate_reads_the_old_keys()
    {
        await using var database = new FixTrackWebApplicationFactory();
        var old = TestCertificates.Create("FixTrack certificado anterior");
        string payload;
        await using (var before = WithCertificate(database, old))
        {
            payload = Protector(before).Protect("sesión anterior");
        }

        await using var rotated = WithCertificate(database, TestCertificates.Create("FixTrack certificado nuevo"), previous: old);
        var rotation = await ProtectAsync(rotated, revoke: false);

        Assert.Equal("sesión anterior", Protector(rotated).Unprotect(payload));
        Assert.Null(rotation.Problem);
        Assert.All(rotation.Keys, key => Assert.True(key.IsReadable));
    }

    [Fact]
    public async Task A_lost_certificate_needs_an_explicit_revocation_before_a_new_one_takes_over()
    {
        await using var database = new FixTrackWebApplicationFactory();
        Guid lostKey;
        await using (var lost = WithCertificate(database, TestCertificates.Create("FixTrack certificado perdido")))
        {
            lostKey = KeyOf(Protector(lost).Protect("sesión con el certificado perdido"));
        }

        var replacement = TestCertificates.Create("FixTrack certificado de reemplazo");

        // proteger-claves con el certificado nuevo: sin la opción, no toca nada. Los comandos terminan antes de la
        // comprobación de arranque (Program devuelve antes); aquí se omite solo esa comprobación para obtener los servicios.
        await using var command = WithCertificate(database, replacement).WithWebHostBuilder(builder => builder
            .ConfigureTestServices(services => services.AddSingleton(provider => new DataProtectionKeyCheck(
                provider.GetRequiredService<IKeyManager>(),
                provider.GetRequiredService<DataProtectionSetup.Settings>() with { Store = DataProtectionSetup.KeyStore.Local },
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<DataProtectionKeyCheck>>()))));
        var refused = await ProtectWithoutStartingAsync(command, revokeUnreadable: false);
        var replaced = await ProtectWithoutStartingAsync(command, revokeUnreadable: true);
        await using var running = WithCertificate(database, replacement);

        Assert.NotNull(refused.Problem);
        Assert.Contains(DataProtectionKeyCheck.RevokeUnreadableOption, refused.Problem);
        Assert.Null(refused.CreatedKey);
        Assert.Empty(refused.RevokedKeys);
        Assert.Contains(refused.Keys, key => key.Id == lostKey && !key.IsReadable && !key.IsRevoked);
        Assert.Null(replaced.Problem);
        Assert.Equal([lostKey], replaced.RevokedKeys);

        // La instancia con el certificado nuevo arranca (la comprobación ya no encuentra claves ilegibles) y usa otra clave.
        var current = KeyOf(Protector(running).Protect("sesión nueva"));
        Assert.NotEqual(lostKey, current);
        Assert.Contains(replaced.Keys, key => key.Id == current && key.IsEncrypted && key.IsReadable && !key.IsRevoked);
    }

    [Fact]
    public async Task An_expired_certificate_still_protects_and_reads_the_keys()
    {
        await using var database = new FixTrackWebApplicationFactory();
        var expired = TestCertificates.Create("FixTrack certificado vencido", DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddDays(-1));
        await using var first = WithCertificate(database, expired);
        var payload = Protector(first).Protect("sesión");
        await using var restarted = WithCertificate(database, expired);

        Assert.Equal("sesión", Protector(restarted).Unprotect(payload));
        Assert.All(await StoredKeysAsync(restarted), key => Assert.Contains("<encryptedSecret", key.Xml));
    }

    [Theory]
    [InlineData(-1, LogLevel.Warning, "venció el")]
    [InlineData(30, LogLevel.Warning, "vence el")]
    [InlineData(400, LogLevel.Information, "vigente hasta el")]
    public async Task The_certificate_expiration_is_reported_at_startup(int daysLeft, LogLevel level, string text)
    {
        var logs = new LogCapture();
        await using var database = new FixTrackWebApplicationFactory();
        var certificate = TestCertificates.Create("FixTrack vigencia", DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddDays(daysLeft));
        await using var app = WithCertificate(database, certificate, logs: logs);

        using var client = app.CreateClient();

        var entry = Assert.Single(logs.Entries, entry => entry.Category == typeof(DataProtectionKeyCheck).FullName);
        Assert.Equal(level, entry.Level);
        Assert.Contains(text, entry.Message);
    }

    [Fact]
    public async Task The_check_does_not_stop_the_application_when_the_database_does_not_answer()
    {
        var logs = new LogCapture();
        await using var database = new FixTrackWebApplicationFactory();
        await using var app = database.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.AddProvider(logs))
            .ConfigureTestServices(services => services.Configure<KeyManagementOptions>(options => options.XmlRepository = new UnavailableRepository())));

        using var client = app.CreateClient();
        using var health = await client.GetAsync("/salud", Token);

        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
        Assert.Contains(logs.Entries, entry => entry.Category == typeof(DataProtectionKeyCheck).FullName
            && entry.Level == LogLevel.Warning && entry.Message.Contains("la base de datos no respondió", StringComparison.Ordinal));
    }

    private static async Task<DataProtectionKeyRotation.Result> ProtectWithoutStartingAsync(WebApplicationFactory<Program> app, bool revokeUnreadable)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DataProtectionKeyRotation>().ProtectAsync(false, Token, revokeUnreadable);
    }

    private sealed class UnavailableRepository : IXmlRepository
    {
        public IReadOnlyCollection<XElement> GetAllElements() => throw new TimeoutException("La base de datos no respondió (simulado).");

        public void StoreElement(XElement element, string friendlyName) => throw new TimeoutException("La base de datos no respondió (simulado).");
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class LogCapture : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}
