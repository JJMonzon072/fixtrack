using System.Text.Json.Serialization;
using FixTrack.Application.Common;
using FixTrack.Application.Devices;
using FixTrack.Application.ServiceOrders;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Web.Models;

namespace FixTrack.Web.Api;

/*
 * Contratos de la API REST v1 (JSON en español, camelCase). Son DTO explícitos: nunca se serializan entidades de EF Core
 * ni modelos internos. Por diseño no incluyen teléfonos ni correos de clientes o técnicos, datos de cuentas de acceso
 * (nombres de usuario, hashes, sellos o tokens), autores de los cambios, notas del historial ni datos de auditoría.
 * Las fechas son UTC en ISO 8601; los montos, números decimales en quetzales (moneda "GTQ").
 */

/// <summary>Una página de resultados.</summary>
public sealed record ApiPage<T>(
    [property: JsonPropertyName("datos")] IReadOnlyList<T> Data,
    [property: JsonPropertyName("paginacion")] ApiPagination Pagination);

public sealed record ApiPagination(
    [property: JsonPropertyName("pagina")] int Page,
    [property: JsonPropertyName("porPagina")] int PageSize,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("totalPaginas")] int TotalPages)
{
    public static ApiPagination From(IPageInfo page, int pageSize) =>
        new(page.Page, pageSize, page.TotalCount, (int)Math.Ceiling(page.TotalCount / (double)pageSize));
}

/// <summary>Valor de una enumeración: código estable (el mismo de los filtros) y nombre para mostrar.</summary>
public sealed record ApiCode(
    [property: JsonPropertyName("codigo")] string Code,
    [property: JsonPropertyName("nombre")] string Name)
{
    public static ApiCode From(ServiceOrderStatus status) => new(DomainDisplay.Slug(status), DomainDisplay.Name(status));

    public static ApiCode From(ServiceOrderPriority priority) => new(DomainDisplay.Slug(priority), DomainDisplay.Name(priority));

    public static ApiCode From(DeviceType type) => new(DomainDisplay.Slug(type), DomainDisplay.Name(type));
}

public sealed record ApiReference(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("nombre")] string Name);

public sealed record ApiDeviceSummary(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("tipo")] ApiCode Type,
    [property: JsonPropertyName("marca")] string Brand,
    [property: JsonPropertyName("modelo")] string? Model);

/// <summary>Orden en un listado.</summary>
/// <param name="Overdue">Abierta y con la entrega comprometida vencida (misma regla que el resto de FixTrack).</param>
public sealed record ApiOrderSummary(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("numero")] string Number,
    [property: JsonPropertyName("estado")] ApiCode Status,
    [property: JsonPropertyName("prioridad")] ApiCode Priority,
    [property: JsonPropertyName("cliente")] ApiReference Customer,
    [property: JsonPropertyName("equipo")] ApiDeviceSummary Device,
    [property: JsonPropertyName("tecnico")] ApiReference? Technician,
    [property: JsonPropertyName("recibidaEn")] DateTime ReceivedAtUtc,
    [property: JsonPropertyName("entregaComprometidaEn")] DateTime? PromisedAtUtc,
    [property: JsonPropertyName("entregadaEn")] DateTime? DeliveredAtUtc,
    [property: JsonPropertyName("atrasada")] bool Overdue)
{
    public static ApiOrderSummary From(ServiceOrderListItem order, DateTime nowUtc) => new(
        order.Id,
        order.Number,
        ApiCode.From(order.Status),
        ApiCode.From(order.Priority),
        new ApiReference(order.CustomerId, order.CustomerName),
        new ApiDeviceSummary(order.DeviceId, ApiCode.From(order.DeviceType), order.DeviceBrand, order.DeviceModel),
        order.TechnicianId is { } technicianId && order.TechnicianName is { } name ? new ApiReference(technicianId, name) : null,
        order.ReceivedAtUtc,
        order.PromisedAtUtc,
        order.DeliveredAtUtc,
        order.IsOverdueAt(nowUtc));
}

public sealed record ApiDeviceDetail(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("tipo")] ApiCode Type,
    [property: JsonPropertyName("marca")] string Brand,
    [property: JsonPropertyName("modelo")] string? Model,
    [property: JsonPropertyName("numeroDeSerie")] string? SerialNumber);

public sealed record ApiTechnician(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("nombre")] string Name,
    [property: JsonPropertyName("especialidad")] string Specialty,
    [property: JsonPropertyName("asignadoEn")] DateTime AssignedAtUtc);

/// <param name="Variance">Costo final menos estimado; <c>null</c> si falta alguno de los dos.</param>
public sealed record ApiCosts(
    [property: JsonPropertyName("moneda")] string Currency,
    [property: JsonPropertyName("estimado")] decimal? Estimated,
    [property: JsonPropertyName("final")] decimal? Final,
    [property: JsonPropertyName("diferencia")] decimal? Variance);

/// <summary>Cambio de estado del historial, sin autor ni nota.</summary>
public sealed record ApiStatusChange(
    [property: JsonPropertyName("fecha")] DateTime ChangedAtUtc,
    [property: JsonPropertyName("estado")] ApiCode Status,
    [property: JsonPropertyName("estadoAnterior")] ApiCode? PreviousStatus);

/// <summary>Detalle de una orden.</summary>
public sealed record ApiOrderDetail(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("numero")] string Number,
    [property: JsonPropertyName("estado")] ApiCode Status,
    [property: JsonPropertyName("prioridad")] ApiCode Priority,
    [property: JsonPropertyName("cliente")] ApiReference Customer,
    [property: JsonPropertyName("equipo")] ApiDeviceDetail Device,
    [property: JsonPropertyName("tecnico")] ApiTechnician? Technician,
    [property: JsonPropertyName("problemaReportado")] string ReportedIssue,
    [property: JsonPropertyName("observacionesRecepcion")] string? IntakeNotes,
    [property: JsonPropertyName("diagnostico")] string? Diagnosis,
    [property: JsonPropertyName("costos")] ApiCosts Costs,
    [property: JsonPropertyName("recibidaEn")] DateTime ReceivedAtUtc,
    [property: JsonPropertyName("entregaComprometidaEn")] DateTime? PromisedAtUtc,
    [property: JsonPropertyName("entregadaEn")] DateTime? DeliveredAtUtc,
    [property: JsonPropertyName("actualizadaEn")] DateTime? UpdatedAtUtc,
    [property: JsonPropertyName("atrasada")] bool Overdue,
    [property: JsonPropertyName("historial")] IReadOnlyList<ApiStatusChange> History)
{
    public static ApiOrderDetail From(ServiceOrderDetails order, DateTime nowUtc) => new(
        order.Id,
        order.Number,
        ApiCode.From(order.Status),
        ApiCode.From(order.Priority),
        new ApiReference(order.Customer.Id, order.Customer.FullName),
        new ApiDeviceDetail(order.Device.Id, ApiCode.From(order.Device.Type), order.Device.Brand, order.Device.Model, order.Device.SerialNumber),
        order.Technician is { } technician
            ? new ApiTechnician(technician.Id, technician.FullName, technician.Specialty, technician.AssignedAtUtc)
            : null,
        order.ReportedIssue,
        order.IntakeNotes,
        order.Diagnosis,
        new ApiCosts("GTQ", order.EstimatedCost, order.FinalCost, order.CostVariance),
        order.ReceivedAtUtc,
        order.PromisedAtUtc,
        order.DeliveredAtUtc,
        order.UpdatedAtUtc,
        order.IsOverdueAt(nowUtc),
        [
            .. order.History.Select(entry => new ApiStatusChange(
                entry.ChangedAtUtc,
                ApiCode.From(entry.ToStatus),
                entry.FromStatus is { } from ? ApiCode.From(from) : null)),
        ]);
}

/// <summary>Equipo en un listado.</summary>
public sealed record ApiDevice(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("tipo")] ApiCode Type,
    [property: JsonPropertyName("marca")] string Brand,
    [property: JsonPropertyName("modelo")] string? Model,
    [property: JsonPropertyName("numeroDeSerie")] string? SerialNumber,
    [property: JsonPropertyName("cliente")] ApiReference Customer,
    [property: JsonPropertyName("ordenes")] int OrderCount,
    [property: JsonPropertyName("registradoEn")] DateTime CreatedAtUtc)
{
    public static ApiDevice From(DeviceListItem device) => new(
        device.Id,
        ApiCode.From(device.Type),
        device.Brand,
        device.Model,
        device.SerialNumber,
        new ApiReference(device.CustomerId, device.CustomerName),
        device.ServiceOrderCount,
        device.CreatedAtUtc);
}
