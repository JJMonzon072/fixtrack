using FixTrack.Application.Modules;

namespace FixTrack.Web.Navigation;

/// <summary>Controlador e ícono con los que se presenta cada módulo.</summary>
public sealed record ModuleRoute(string Controller, string Icon);

/// <summary>Grupo de módulos en el menú lateral. Un grupo sin título se muestra sin encabezado.</summary>
public sealed record NavigationGroup(string? Title, IReadOnlyList<ModuleKey> Modules);

public static class ModuleNavigation
{
    /// <summary>Estructura del menú lateral.</summary>
    public static IReadOnlyList<NavigationGroup> Groups { get; } =
    [
        new(null, [ModuleKey.Dashboard]),
        new("Operación", [ModuleKey.ServiceOrders, ModuleKey.WorkQueue]),
        new("Gestión", [ModuleKey.Customers, ModuleKey.Devices, ModuleKey.Technicians]),
    ];

    public static ModuleRoute For(ModuleKey key) => key switch
    {
        ModuleKey.Dashboard => new("Dashboard", "layout-dashboard"),
        ModuleKey.ServiceOrders => new("ServiceOrders", "clipboard-list"),
        ModuleKey.WorkQueue => new("WorkQueue", "list-ordered"),
        ModuleKey.Customers => new("Customers", "users"),
        ModuleKey.Devices => new("Devices", "laptop"),
        ModuleKey.Technicians => new("Technicians", "wrench"),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Módulo sin ruta asignada."),
    };
}
