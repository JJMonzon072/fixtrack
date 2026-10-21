using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FixTrack.Demo;

/// <summary>
/// Configuración de la demostración (sección <c>Demo</c>, por ejemplo la variable <c>Demo__AppPassword</c>). La
/// herramienta nunca recibe una cadena de conexión completa: la arma a partir de un servidor local y de una base cuyo
/// nombre empieza con <see cref="DatabasePrefix"/>, de modo que no puede apuntar a la base real del proyecto.
/// </summary>
internal sealed partial record DemoSettings(
    string Server,
    int Port,
    string Database,
    string AppLogin,
    string? SaPassword,
    string? AppPassword,
    string CertificatePath,
    string? CertificatePassword,
    string? AdminPassword,
    string? OperatorPassword,
    string? ReadOnlyPassword)
{
    public const string SectionName = "Demo";
    public const string DatabasePrefix = "FixTrackDemo";

    /// <summary>Servidores aceptados: el servicio de Docker Compose o este mismo equipo.</summary>
    public static IReadOnlyList<string> AllowedServers { get; } = ["sqlserver", "localhost", "127.0.0.1"];

    public static DemoSettings From(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var port = section["Port"] is { Length: > 0 } text
            ? int.TryParse(text, out var value) ? value : -1
            : 1433;
        return new DemoSettings(
            section["Server"] ?? "sqlserver",
            port,
            section["Database"] ?? DatabasePrefix,
            section["AppLogin"] ?? "fixtrack_demo",
            section["SaPassword"],
            section["AppPassword"],
            section["CertificatePath"] ?? "/certificado/fixtrack-demo-dp.pfx",
            section["CertificatePassword"],
            section["AdminPassword"],
            section["OperatorPassword"],
            section["ReadOnlyPassword"]);
    }

    /// <summary>Problemas que impiden usar el destino configurado; vacío si es una base local de demostración.</summary>
    public IReadOnlyList<string> TargetProblems()
    {
        var problems = new List<string>();
        if (!AllowedServers.Contains(Server, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"Demo:Server debe ser un servidor local ({string.Join(", ", AllowedServers)}); se recibió «{Server}».");
        }

        if (Port is < 1 or > 65_535)
        {
            problems.Add("Demo:Port debe ser un puerto entre 1 y 65535.");
        }

        if (!DatabasePattern().IsMatch(Database))
        {
            problems.Add($"Demo:Database debe empezar con {DatabasePrefix} y tener solo letras, números o guiones bajos; se recibió «{Database}».");
        }

        if (!LoginPattern().IsMatch(AppLogin) || AppLogin.Equals("sa", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Demo:AppLogin debe ser un nombre de 3 a 32 letras minúsculas, números o guiones bajos, distinto de sa.");
        }

        return problems;
    }

    /// <summary>Conexión del administrador del servidor (sa) a master: solo para crear la base y el login de la aplicación.</summary>
    public string ServerAdminConnectionString() => Build("master", "sa", Required(SaPassword, "SaPassword"));

    /// <summary>Conexión de la aplicación a la base de demostración, con su propio login (nunca sa).</summary>
    public string AppConnectionString() => Build(Database, AppLogin, Required(AppPassword, "AppPassword"));

    public static string Required(string? value, string name) =>
        string.IsNullOrEmpty(value) ? throw new DemoException($"Falta Demo:{name} (variable de entorno Demo__{name}).") : value;

    private string Build(string database, string user, string password)
    {
        if (TargetProblems() is { Count: > 0 } problems)
        {
            throw new DemoException(string.Join(Environment.NewLine, problems));
        }

        // Cifrado activo; el certificado de un contenedor local es autofirmado, por eso se confía en él explícitamente.
        return new SqlConnectionStringBuilder
        {
            DataSource = Port == 1433 ? Server : $"{Server},{Port}",
            InitialCatalog = database,
            UserID = user,
            Password = password,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = true,
            ApplicationName = "FixTrack.Demo",
        }.ConnectionString;
    }

    [GeneratedRegex("^FixTrackDemo[A-Za-z0-9_]{0,50}$")]
    private static partial Regex DatabasePattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{2,31}$")]
    private static partial Regex LoginPattern();
}

/// <summary>Error esperado de la herramienta: se informa con su mensaje, sin traza.</summary>
internal sealed class DemoException(string message) : Exception(message);
