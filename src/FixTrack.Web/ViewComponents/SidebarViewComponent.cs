using FixTrack.Application.Modules;
using FixTrack.Web.Models;
using FixTrack.Web.Navigation;
using FixTrack.Web.Portfolio;
using FixTrack.Web.Security;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FixTrack.Web.ViewComponents;

/// <summary>
/// Menú lateral construido a partir del catálogo de módulos. Solo muestra los módulos que la cuenta puede usar;
/// la autorización real la aplican los controladores.
/// </summary>
public sealed class SidebarViewComponent(
    IModuleCatalog modules,
    ApplicationInfo application,
    IAuthorizationService authorization,
    IOptions<PortfolioFeatures> portfolio) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var currentController = RouteData.Values["controller"] as string;
        var groups = new List<SidebarGroupViewModel>();

        foreach (var group in ModuleNavigation.Groups)
        {
            var items = new List<SidebarItemViewModel>();
            foreach (var key in group.Modules)
            {
                if (ModuleNavigation.RequiredPolicy(key) is { } policy
                    && !(await authorization.AuthorizeAsync(UserClaimsPrincipal, policy)).Succeeded)
                {
                    continue;
                }

                items.Add(CreateItem(key, currentController));
            }

            if (items.Count > 0)
            {
                groups.Add(new SidebarGroupViewModel(group.Title, items));
            }
        }

        // Extra de portafolio: solo con PortfolioFeatures:Reports habilitado. Desactivado, el menú es el de siempre.
        if (portfolio.Value.Reports && (await authorization.AuthorizeAsync(UserClaimsPrincipal, AuthorizationPolicies.Workshop)).Succeeded)
        {
            groups.Add(new SidebarGroupViewModel(
                "Análisis",
                [new SidebarItemViewModel("Reportes", "Reports", "chart-column", IsAvailable: true, IsActive: currentController == "Reports")]));
        }

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
