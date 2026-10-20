using System.Reflection;
using System.Runtime.InteropServices;

namespace FixTrack.Web.Services;

/// <summary>Datos reales de la instancia en ejecución, mostrados en la interfaz.</summary>
public sealed class ApplicationInfo
{
    public ApplicationInfo(IWebHostEnvironment environment)
    {
        var informationalVersion = typeof(ApplicationInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0";

        // La versión informativa incluye el hash del commit tras '+'; solo se muestra la versión.
        Version = informationalVersion.Split('+')[0];
        Runtime = RuntimeInformation.FrameworkDescription;
        IsDevelopment = environment.IsDevelopment();
        EnvironmentName = environment.EnvironmentName switch
        {
            "Development" => "Desarrollo",
            "Staging" => "Preproducción",
            "Production" => "Producción",
            var other => other,
        };
    }

    public string Version { get; }

    public string Runtime { get; }

    public string EnvironmentName { get; }

    public bool IsDevelopment { get; }
}
