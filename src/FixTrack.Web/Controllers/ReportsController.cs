using System.Globalization;
using FixTrack.Application.Reports;
using FixTrack.Application.Security;
using FixTrack.Application.Technicians;
using FixTrack.Web.Portfolio;
using FixTrack.Web.Reports;
using FixTrack.Web.Security;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FixTrack.Web.Controllers;

/// <summary>
/// Centro de reportes (extra de portafolio, <c>PortfolioFeatures:Reports</c>). Lo ven los tres roles; la cuenta de
/// consulta, como en todo FixTrack, solo con solicitudes GET. La exportación CSV, que descarga datos de clientes en
/// bloque, queda para Administrador y Operador.
/// </summary>
[Route("reportes")]
[Authorize(Policy = AuthorizationPolicies.Workshop)]
[PortfolioFeature(PortfolioFeature.Reports)]
public sealed class ReportsController(
    IReportService reports,
    ITechnicianService technicians,
    IOptions<PortfolioFeatures> features,
    TimeProvider clock) : Controller
{
    /// <summary>Roles que pueden exportar: el rol Consulta no descarga datos en bloque.</summary>
    public const string ExportRoles = Roles.Administrator + "," + Roles.Operator;

    private DateOnly Today => ReportPeriod.Today(clock);

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "rango")] string? range,
        [FromQuery(Name = "desde")] string? from,
        [FromQuery(Name = "hasta")] string? to,
        [FromQuery(Name = "estado")] string? status,
        [FromQuery(Name = "prioridad")] string? priority,
        [FromQuery(Name = "tecnico")] string? technician,
        CancellationToken cancellationToken)
    {
        var query = new ReportQuery(range, from, to, status, priority, technician);
        var workload = await technicians.ListWorkloadAsync(cancellationToken);
        var filter = query.TryCreateFilter(Today, clock.LocalTimeZone, id => workload.Any(item => item.Id == id), out var errors);
        var report = filter is null ? null : await reports.GetAsync(filter, cancellationToken);
        if (report is null)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }

        return View(new ReportsViewModel(
            query,
            report,
            errors,
            workload,
            Today,
            CanExport: User.IsInRole(Roles.Administrator) || User.IsInRole(Roles.Operator),
            features.Value.ExportMaxRows));
    }

    /// <param name="basis"><c>recepcion</c> (predeterminado) o <c>entrega</c>: qué fecha selecciona las órdenes.</param>
    [HttpGet("exportar")]
    [Authorize(Roles = ExportRoles)]
    public async Task<IActionResult> Export(
        [FromQuery(Name = "rango")] string? range,
        [FromQuery(Name = "desde")] string? from,
        [FromQuery(Name = "hasta")] string? to,
        [FromQuery(Name = "estado")] string? status,
        [FromQuery(Name = "prioridad")] string? priority,
        [FromQuery(Name = "tecnico")] string? technician,
        [FromQuery(Name = "por")] string? basis,
        CancellationToken cancellationToken)
    {
        var routeValues = new { rango = range, desde = from, hasta = to, estado = status, prioridad = priority, tecnico = technician };
        var query = new ReportQuery(range, from, to, status, priority, technician);
        var workload = await technicians.ListWorkloadAsync(cancellationToken);
        var filter = query.TryCreateFilter(Today, clock.LocalTimeZone, id => workload.Any(item => item.Id == id), out _);
        ReportExportBasis? exportBasis = basis?.Trim().ToLowerInvariant() switch
        {
            null or "" or "recepcion" => ReportExportBasis.Received,
            "entrega" => ReportExportBasis.Delivered,
            _ => null,
        };

        if (filter is null || exportBasis is null)
        {
            // La página del reporte explica qué filtro no es válido.
            StatusMessage.Warning(TempData, "No se pudo exportar: revisa el período y los filtros.");
            return RedirectToAction(nameof(Index), routeValues);
        }

        var maxRows = features.Value.ExportMaxRows;
        var export = await reports.ExportAsync(filter, exportBasis.Value, maxRows, cancellationToken);
        if (export.TooLarge)
        {
            StatusMessage.Warning(
                TempData,
                $"La exportación supera el máximo de {maxRows.ToString("N0", CultureInfo.InvariantCulture)} órdenes y no se generó. Reduce el período o agrega filtros.");
            return RedirectToAction(nameof(Index), routeValues);
        }

        // El archivo se genera en memoria para esta descarga y no se guarda en el servidor ni en la caché.
        Response.Headers.CacheControl = "no-store";
        return File(OrderCsv.Write(export.Rows, clock.LocalTimeZone), Csv.ContentType, OrderCsv.FileName(exportBasis.Value, filter.Period));
    }
}
