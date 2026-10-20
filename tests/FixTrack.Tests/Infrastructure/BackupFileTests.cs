using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixTrack.Infrastructure.Persistence.Backups;

namespace FixTrack.Tests.Infrastructure;

/// <summary>Reglas del archivo de respaldo que no necesitan una base: dónde se escribe y cómo se detecta un daño.</summary>
public sealed class BackupFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fixtrack-respaldo-archivo-" + Guid.NewGuid().ToString("N"));

    public BackupFileTests() => Directory.CreateDirectory(_folder);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task Backups_are_never_written_inside_a_git_repository()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "repositorio", ".git"));
        var inside = Path.Combine(_folder, "repositorio", "respaldos");
        using var database = new SqliteTestDatabase();
        await using var context = database.CreateContext();

        var error = await Assert.ThrowsAsync<BackupException>(() =>
            new DatabaseBackup(context, TimeProvider.System).ExportAsync(inside, Token));

        Assert.Contains("repositorio Git", error.Message);
        Assert.False(Directory.Exists(inside));
        DatabaseBackup.EnsureOutsideRepository(Path.Combine(_folder, "fuera"));
    }

    [Fact]
    public async Task An_intact_backup_passes_verification()
    {
        var file = WriteBackup("[1,\"Ana\"]\n[2,\"Luis\"]\n", declaredRows: 2);

        var manifest = await DatabaseBackup.VerifyAsync(file, Token);

        Assert.Equal(2, manifest.TotalRows);
    }

    [Theory]
    [InlineData("[1,\"Ana\"]\n[2,\"Lui5\"]\n", 2)]
    [InlineData("[1,\"Ana\"]\n", 2)]
    public async Task A_damaged_or_incomplete_backup_is_rejected(string content, int declaredRows)
    {
        var file = WriteBackup(content, declaredRows, hashOf: "[1,\"Ana\"]\n[2,\"Luis\"]\n");

        var error = await Assert.ThrowsAsync<BackupException>(() => DatabaseBackup.VerifyAsync(file, Token));

        Assert.Contains("dañado", error.Message);
    }

    [Fact]
    public async Task A_file_that_is_not_a_backup_is_rejected_with_a_clear_message()
    {
        var file = Path.Combine(_folder, "otro.zip");
        await File.WriteAllTextAsync(file, "no es un zip", Token);

        var error = await Assert.ThrowsAsync<BackupException>(() => DatabaseBackup.VerifyAsync(file, Token));

        Assert.Contains("no es un respaldo", error.Message);
    }

    private string WriteBackup(string content, int declaredRows, string? hashOf = null)
    {
        var file = Path.Combine(_folder, $"respaldo-{Guid.NewGuid():N}.zip");
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(hashOf ?? content)));
        var table = new BackupTable("Clientes", [new BackupColumn("Id", "int", true), new BackupColumn("Nombre", "nvarchar", false)], declaredRows, sha);
        var manifest = new BackupManifest(BackupManifest.CurrentFormat, DateTime.UtcNow, "Prueba", "snapshot", ["20261020031151_InitialCreate"], [table]);

        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry(table.EntryName).Open(), new UTF8Encoding(false)))
        {
            writer.Write(content);
        }

        using (var stream = zip.CreateEntry(BackupManifest.EntryName).Open())
        {
            JsonSerializer.Serialize(stream, manifest, BackupFormat.ManifestJson);
        }

        return file;
    }
}
