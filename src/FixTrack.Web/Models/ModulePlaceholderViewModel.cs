using FixTrack.Application.Modules;

namespace FixTrack.Web.Models;

/// <summary>Página provisional de un módulo que todavía no está implementado.</summary>
/// <param name="Module">Módulo representado.</param>
/// <param name="EmptyTitle">Título del estado vacío.</param>
/// <param name="CreateLabel">Texto de la acción principal que tendrá el módulo.</param>
/// <param name="Columns">Columnas previstas para el listado.</param>
public sealed record ModulePlaceholderViewModel(
    ModuleInfo Module,
    string EmptyTitle,
    string CreateLabel,
    IReadOnlyList<string> Columns);
