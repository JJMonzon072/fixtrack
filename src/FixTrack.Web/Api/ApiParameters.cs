using System.Globalization;
using FixTrack.Application.ReadApi;
using FixTrack.Application.Reports;
using FixTrack.Web.Models;
using FixTrack.Web.Reports;

namespace FixTrack.Web.Api;

/// <summary>
/// Validación de los parámetros de la API. Todos llegan como texto y se validan aquí: los errores se devuelven con el
/// nombre del parámetro y un mensaje en español (400 con <c>errors</c>). Los parámetros desconocidos se ignoran.
/// </summary>
public sealed class ApiParameters
{
    private readonly Dictionary<string, string[]> _errors = [];

    public IReadOnlyDictionary<string, string[]> Errors => _errors;

    public bool IsValid => _errors.Count == 0;

    /// <summary>Valores aceptados en <c>orden</c> para las órdenes (con "-" delante, descendente).</summary>
    public static IReadOnlyDictionary<string, OrderSortField> OrderSorts { get; } = new Dictionary<string, OrderSortField>
    {
        ["recibida"] = OrderSortField.Received,
        ["comprometida"] = OrderSortField.Promised,
        ["prioridad"] = OrderSortField.Priority,
        ["numero"] = OrderSortField.Number,
    };

    /// <summary>Valores aceptados en <c>orden</c> para los equipos.</summary>
    public static IReadOnlyDictionary<string, DeviceSortField> DeviceSorts { get; } = new Dictionary<string, DeviceSortField>
    {
        ["registrado"] = DeviceSortField.Registered,
        ["marca"] = DeviceSortField.Brand,
    };

    public OrderListQuery Orders(
        string? page,
        string? pageSize,
        string? status,
        string? priority,
        string? technician,
        string? device,
        string? customer,
        string? receivedFrom,
        string? receivedTo,
        string? sort,
        TimeZoneInfo timeZone)
    {
        var from = Date(receivedFrom, "recibidaDesde");
        var to = Date(receivedTo, "recibidaHasta");
        if (from is not null && to is not null && to < from)
        {
            Add("recibidaHasta", "Debe ser igual o posterior a recibidaDesde.");
        }

        var (sortField, descending) = Sort(sort, OrderSorts, "-recibida");
        var technicianText = technician?.Trim();
        var unassigned = string.Equals(technicianText, ReportQuery.UnassignedSlug, StringComparison.OrdinalIgnoreCase);
        return new OrderListQuery(
            Page(page),
            PageSize(pageSize),
            Optional(status, "estado", DomainDisplay.ParseStatus, "Usa recibida, en-diagnostico, en-reparacion, en-espera-de-repuestos, lista-para-entrega, entregada o cancelada."),
            Optional(priority, "prioridad", DomainDisplay.ParsePriority, "Usa baja, normal, alta o urgente."),
            unassigned ? null : Id(technicianText, "tecnico", "Usa el Id de un técnico o sin-asignar."),
            unassigned,
            Id(device, "equipo"),
            Id(customer, "cliente"),
            from is { } start ? ReportPeriod.StartOfDayUtc(start, timeZone) : null,
            to is { } end ? ReportPeriod.StartOfDayUtc(end.AddDays(1), timeZone) : null,
            sortField,
            descending);
    }

    public DeviceListQuery Devices(string? page, string? pageSize, string? type, string? customer, string? sort)
    {
        var (sortField, descending) = Sort(sort, DeviceSorts, "-registrado");
        return new DeviceListQuery(
            Page(page),
            PageSize(pageSize),
            Optional(type, "tipo", DomainDisplay.ParseDeviceType, "Usa laptop, escritorio, todo-en-uno, tableta, impresora, monitor u otro."),
            Id(customer, "cliente"),
            sortField,
            descending);
    }

    /// <summary>Agrega los errores del filtro de un período (claves de <see cref="ReportQuery"/>).</summary>
    public void AddAll(IReadOnlyDictionary<string, string> errors)
    {
        foreach (var (key, message) in errors)
        {
            Add(key, message);
        }
    }

    private int Page(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 1;
        }

        if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var page) || page is < 1 or > ReadApiLimits.MaxPage)
        {
            Add("pagina", $"Debe ser un número entero entre 1 y {ReadApiLimits.MaxPage}.");
            return 1;
        }

        return page;
    }

    private int PageSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ReadApiLimits.DefaultPageSize;
        }

        if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size is < 1 or > ReadApiLimits.MaxPageSize)
        {
            Add("porPagina", $"Debe ser un número entero entre 1 y {ReadApiLimits.MaxPageSize}.");
            return ReadApiLimits.DefaultPageSize;
        }

        return size;
    }

    private T? Optional<T>(string? value, string key, Func<string?, T?> parse, string hint)
        where T : struct
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsed = parse(value);
        if (parsed is null)
        {
            Add(key, $"El valor «{value.Trim()}» no es válido. {hint}");
        }

        return parsed;
    }

    private int? Id(string? value, string key, string hint = "Debe ser un Id numérico positivo.")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id;
        }

        Add(key, hint);
        return null;
    }

    private DateOnly? Date(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(value.Trim(), ReportQuery.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date >= ReportPeriod.MinDate && date.Year <= 2100)
        {
            return date;
        }

        Add(key, "Escribe la fecha con el formato AAAA-MM-DD (hora de Guatemala).");
        return null;
    }

    private (T Field, bool Descending) Sort<T>(string? value, IReadOnlyDictionary<string, T> fields, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        var descending = text.StartsWith('-');
        if (fields.TryGetValue(descending ? text[1..] : text, out var field))
        {
            return (field, descending);
        }

        Add("orden", $"Usa uno de estos valores (con «-» delante para orden descendente): {string.Join(", ", fields.Keys)}.");
        return (fields[fallback.TrimStart('-')], fallback.StartsWith('-'));
    }

    private void Add(string key, string message) => _errors[key] = [message];
}
