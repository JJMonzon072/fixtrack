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
                ModuleStatus.Planned,
                [
                    "Registro de órdenes con diagnóstico inicial",
                    "Asignación de técnicos, estados y prioridades",
                    "Cálculo de costos y priorización por fecha de entrega",
                ]),
            new ModuleInfo(
                ModuleKey.Customers,
                "Clientes",
                "Datos de contacto de cada cliente y su historial de servicios.",
                ModuleStatus.Planned,
                [
                    "Registro y edición de clientes",
                    "Consulta de los equipos de cada cliente",
                    "Historial de servicios por cliente",
                ]),
            new ModuleInfo(
                ModuleKey.Devices,
                "Equipos",
                "Computadoras, laptops y otros dispositivos recibidos, vinculados a su propietario.",
                ModuleStatus.Planned,
                [
                    "Registro por tipo, marca, modelo y número de serie",
                    "Vinculación con el cliente propietario",
                    "Historial de reparaciones por equipo",
                ]),
            new ModuleInfo(
                ModuleKey.Technicians,
                "Técnicos",
                "Personal técnico del taller y su carga de trabajo.",
                ModuleStatus.Planned,
                [
                    "Registro de técnicos y especialidades",
                    "Asignación a órdenes de servicio",
                    "Seguimiento de la carga de trabajo",
                ]),
        ];

        _modulesByKey = Modules.ToDictionary(module => module.Key);
    }

    public IReadOnlyList<ModuleInfo> Modules { get; }

    public ModuleInfo Get(ModuleKey key) =>
        _modulesByKey.TryGetValue(key, out var module)
            ? module
            : throw new ArgumentOutOfRangeException(nameof(key), key, "El módulo no está registrado en el catálogo.");
}
