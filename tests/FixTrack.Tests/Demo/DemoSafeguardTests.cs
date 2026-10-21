using System.Security.Cryptography.X509Certificates;
using FixTrack.Demo;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FixTrack.Tests.Demo;

/// <summary>
/// La herramienta de la demostración solo trabaja con un SQL Server local y una base FixTrackDemo*: no puede apuntar a
/// la base real del proyecto aunque se le pase su nombre o su servidor.
/// </summary>
public sealed class DemoSafeguardTests
{
    private static DemoSettings Settings(params (string Key, string Value)[] values) =>
        DemoSettings.From(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>($"Demo:{value.Key}", value.Value)))
            .Build());

    [Fact]
    public void By_default_it_targets_the_compose_service_and_a_demo_database()
    {
        var settings = Settings(("AppPassword", "ClaveDeLaApp123"));

        Assert.Empty(settings.TargetProblems());
        var connection = new SqlConnectionStringBuilder(settings.AppConnectionString());
        Assert.Equal("sqlserver", connection.DataSource);
        Assert.Equal("FixTrackDemo", connection.InitialCatalog);
        Assert.Equal("fixtrack_demo", connection.UserID);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, connection.Encrypt);
    }

    [Theory]
    [InlineData("localhost", "14333", "FixTrackDemo")]
    [InlineData("127.0.0.1", "1433", "FixTrackDemoPrueba")]
    [InlineData("SQLSERVER", "1433", "FixTrackDemo_2")]
    public void Accepts_local_servers_and_demo_databases(string server, string port, string database)
    {
        var settings = Settings(("Server", server), ("Port", port), ("Database", database), ("SaPassword", "ClaveDelServidor123"));

        Assert.Empty(settings.TargetProblems());
        Assert.Equal("master", new SqlConnectionStringBuilder(settings.ServerAdminConnectionString()).InitialCatalog);
    }

    [Theory]
    [InlineData("Server", "FixTrackDB.mssql.somee.com")]
    [InlineData("Server", "10.0.0.5")]
    [InlineData("Server", "sqlserver.example.com")]
    [InlineData("Database", "FixTrackDB")]
    [InlineData("Database", "master")]
    [InlineData("Database", "FixTrackDemo]; DROP DATABASE [FixTrackDB")]
    [InlineData("AppLogin", "sa")]
    [InlineData("AppLogin", "x")]
    [InlineData("Port", "no-es-un-puerto")]
    public void Rejects_anything_that_is_not_a_local_demo_database(string key, string value)
    {
        var settings = Settings((key, value), ("AppPassword", "ClaveDeLaApp123"), ("SaPassword", "ClaveDelServidor123"));

        Assert.NotEmpty(settings.TargetProblems());
        Assert.Throws<DemoException>(settings.AppConnectionString);
        Assert.Throws<DemoException>(settings.ServerAdminConnectionString);
    }

    [Fact]
    public void A_missing_password_names_the_variable_without_showing_values()
    {
        var settings = Settings(("SaPassword", "ClaveDelServidor123"));

        var error = Assert.Throws<DemoException>(settings.AppConnectionString);

        Assert.Contains("Demo__AppPassword", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaveDelServidor123", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_certificate_is_created_once_and_only_its_owner_can_read_it()
    {
        var folder = Directory.CreateTempSubdirectory("fixtrack-demo-");
        try
        {
            var path = Path.Combine(folder.FullName, "dp.pfx");

            Assert.True(DemoCertificate.Ensure(path, "ClaveDelCertificado123"));
            Assert.False(DemoCertificate.Ensure(path, "ClaveDelCertificado123"));

            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, "ClaveDelCertificado123", X509KeyStorageFlags.Exportable);
            using var key = certificate.GetRSAPrivateKey();
            Assert.NotNull(key);
            Assert.Equal(3072, key.KeySize);
            Assert.Equal(DemoCertificate.Subject, certificate.Subject);
            Assert.True(certificate.NotAfter > DateTime.Now.AddYears(1));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            }

            var error = Assert.Throws<DemoException>(() => DemoCertificate.Ensure(path, "OtraClave123456"));
            Assert.Contains("reiniciar", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
