namespace FixTrack.Web.Models;

public sealed record SidebarViewModel(IReadOnlyList<SidebarGroupViewModel> Groups, string Version);

public sealed record SidebarGroupViewModel(string? Title, IReadOnlyList<SidebarItemViewModel> Items);

public sealed record SidebarItemViewModel(
    string Name,
    string Controller,
    string Icon,
    bool IsAvailable,
    bool IsActive);
