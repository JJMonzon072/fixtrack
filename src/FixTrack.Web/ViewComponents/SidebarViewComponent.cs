using FixTrack.Application.Modules;
using FixTrack.Web.Models;
using FixTrack.Web.Navigation;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.ViewComponents;

/// <summary>Menú lateral construido a partir del catálogo de módulos.</summary>
public sealed class SidebarViewComponent(IModuleCatalog modules, ApplicationInfo application) : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        var currentController = RouteData.Values["controller"] as string;

        var groups = ModuleNavigation.Groups
            .Select(group => new SidebarGroupViewModel(
                group.Title,
                [.. group.Modules.Select(key => CreateItem(key, currentController))]))
            .ToList();

        return View(new SidebarViewModel(groups, application.Version));
    }

    private SidebarItemViewModel CreateItem(ModuleKey key, string? currentController)
    {
        var module = modules.Get(key);
        var route = ModuleNavigation.For(key);

        return new SidebarItemViewModel(
            module.Name,
            route.Controller,
            route.Icon,
            module.IsAvailable,
            IsActive: string.Equals(route.Controller, currentController, StringComparison.OrdinalIgnoreCase));
    }
}
