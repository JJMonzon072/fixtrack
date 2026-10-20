namespace FixTrack.Application.Modules;

/// <summary>
/// Fuente única de los módulos del sistema y de su disponibilidad. La navegación, el panel
/// y las páginas de cada módulo se construyen a partir de este catálogo.
/// </summary>
public interface IModuleCatalog
{
    /// <summary>Todos los módulos, en el orden en que se presentan en la navegación.</summary>
    IReadOnlyList<ModuleInfo> Modules { get; }

    /// <summary>Obtiene la descripción de un módulo.</summary>
    ModuleInfo Get(ModuleKey key);
}
