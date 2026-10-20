using System.Globalization;
using FixTrack.Application.Dashboard;
using FixTrack.Application.Modules;
using FixTrack.Web.Models;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>Panel general: indicadores, acciones recomendadas y listas calculadas con los datos registrados.</summary>
[Route("")]
public sealed class DashboardController(
    IDashboardService dashboard,
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
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var culture = CultureInfo.CurrentCulture;
        var today = clock.GetLocalNow().ToString("dddd, d 'de' MMMM 'de' yyyy", culture);

        var model = new DashboardViewModel(
            Today: culture.TextInfo.ToUpper(today[0]) + today[1..],
            Summary: await dashboard.GetAsync(cancellationToken),
            SetupSteps: [.. SetupOrder.Select(modules.Get)],
            Application: application);

        return View(model);
    }
}
