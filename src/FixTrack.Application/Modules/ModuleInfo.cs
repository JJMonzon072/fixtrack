namespace FixTrack.Application.Modules;

/// <summary>Describe un módulo funcional del sistema y su estado de implementación.</summary>
/// <param name="Key">Identificador del módulo.</param>
/// <param name="Name">Nombre visible del módulo.</param>
/// <param name="Description">Propósito del módulo en una frase.</param>
/// <param name="Status">Estado de implementación.</param>
/// <param name="PlannedFeatures">Funcionalidades previstas mientras el módulo no esté disponible.</param>
public sealed record ModuleInfo(
    ModuleKey Key,
    string Name,
    string Description,
    ModuleStatus Status,
    IReadOnlyList<string> PlannedFeatures)
{
    public bool IsAvailable => Status == ModuleStatus.Available;
}
