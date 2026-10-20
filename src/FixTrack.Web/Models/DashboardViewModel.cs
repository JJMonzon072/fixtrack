using FixTrack.Application.Modules;
using FixTrack.Web.Services;

namespace FixTrack.Web.Models;

/// <param name="Today">Fecha actual, ya formateada en español.</param>
/// <param name="SetupSteps">Módulos en el orden recomendado para comenzar a operar.</param>
/// <param name="AvailableModules">Cantidad de módulos implementados.</param>
/// <param name="TotalModules">Cantidad total de módulos previstos.</param>
/// <param name="Application">Datos de la instancia en ejecución.</param>
public sealed record DashboardViewModel(
    string Today,
    IReadOnlyList<ModuleInfo> SetupSteps,
    int AvailableModules,
    int TotalModules,
    ApplicationInfo Application);
