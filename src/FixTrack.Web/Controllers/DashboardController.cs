using System.Globalization;
using FixTrack.Application.Modules;
using FixTrack.Web.Models;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

[Route("")]
public sealed class DashboardController(
    IModuleCatalog modules,
    ApplicationInfo application,
    TimeProvider clock) : Controller
{
    // Orden recomendado para comenzar a operar: una orden necesita cliente, equipo y técnico.
    private static readonly ModuleKey[] SetupOrder =
    [
        ModuleKey.Technicians,
        ModuleKey.Customers,
        ModuleKey.Devices,
        ModuleKey.ServiceOrders,
    ];

    [HttpGet("")]
    public IActionResult Index()
    {
        var culture = CultureInfo.CurrentCulture;
        var today = clock.GetLocalNow().ToString("dddd, d 'de' MMMM 'de' yyyy", culture);

        var model = new DashboardViewModel(
            Today: culture.TextInfo.ToUpper(today[0]) + today[1..],
            SetupSteps: [.. SetupOrder.Select(modules.Get)],
            AvailableModules: modules.Modules.Count(module => module.IsAvailable),
            TotalModules: modules.Modules.Count,
            Application: application);

        return View(model);
    }
}
