namespace FixTrack.Application.Modules;

/// <summary>
/// Catálogo de módulos de FixTrack. Al implementar un módulo en una fase posterior,
/// basta con cambiar aquí su estado a <see cref="ModuleStatus.Available"/>.
/// </summary>
internal sealed class ModuleCatalog : IModuleCatalog
{
    private readonly Dictionary<ModuleKey, ModuleInfo> _modulesByKey;

    public ModuleCatalog()
    {
        Modules =
        [
            new ModuleInfo(
                ModuleKey.Dashboard,
                "Panel",
                "Resumen general de la operación del taller.",
                ModuleStatus.Available,
                []),
            new ModuleInfo(
                ModuleKey.ServiceOrders,
                "Órdenes de servicio",
                "Registro y seguimiento de cada reparación, desde la recepción del equipo hasta su entrega.",
                ModuleStatus.Available,
                []),
            new ModuleInfo(
                ModuleKey.WorkQueue,
                "Cola de trabajo",
                "Órdenes abiertas ordenadas por el algoritmo de priorización, con la acción recomendada para cada una.",
                ModuleStatus.Available,
                []),
            new ModuleInfo(
                ModuleKey.Customers,
                "Clientes",
                "Registro, búsqueda y datos de contacto de los clientes del taller.",
                ModuleStatus.Available,
                []),
            new ModuleInfo(
                ModuleKey.Devices,
                "Equipos",
                "Computadoras, laptops y otros dispositivos recibidos, vinculados a su propietario.",
                ModuleStatus.Available,
                []),
            new ModuleInfo(
                ModuleKey.Technicians,
                "Técnicos",
                "Personal técnico del taller, su especialidad y su disponibilidad.",
                ModuleStatus.Available,
                []),
            new ModuleInfo(
                ModuleKey.Users,
                "Usuarios",
                "Cuentas de acceso al sistema y sus roles. Solo para administradores.",
                ModuleStatus.Available,
                []),
            new ModuleInfo(
                ModuleKey.Audit,
                "Auditoría",
                "Quién hizo cada operación importante y cuándo. Solo para administradores.",
                ModuleStatus.Available,
                []),
        ];

        _modulesByKey = Modules.ToDictionary(module => module.Key);
    }

    public IReadOnlyList<ModuleInfo> Modules { get; }

    public ModuleInfo Get(ModuleKey key) =>
        _modulesByKey.TryGetValue(key, out var module)
            ? module
            : throw new ArgumentOutOfRangeException(nameof(key), key, "El módulo no está registrado en el catálogo.");
}
