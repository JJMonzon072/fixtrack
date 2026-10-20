using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Domain.Devices;
using Microsoft.EntityFrameworkCore;

namespace FixTrack.Infrastructure.Persistence.Repositories;

/// <summary>Consultas de equipos con EF Core: búsqueda, filtro y paginación se resuelven en SQL.</summary>
internal sealed class DeviceRepository(FixTrackDbContext db) : IDeviceRepository
{
    public async Task<PagedResult<DeviceListItem>> SearchAsync(DeviceSearch search, CancellationToken cancellationToken)
    {
        var matching = db.Devices.AsNoTracking();
        if (search.Type is { } type)
        {
            matching = matching.Where(device => device.Type == type);
        }

        if (search.Text is { } text)
        {
            var pattern = LikePatterns.Contains(text);
            matching = matching.Where(device =>
                EF.Functions.Like(device.Brand, pattern, LikePatterns.Escape)
                || (device.Model != null && EF.Functions.Like(device.Model, pattern, LikePatterns.Escape))
                || EF.Functions.Like(device.Brand + " " + (device.Model ?? string.Empty), pattern, LikePatterns.Escape)
                || (device.SerialNumber != null && EF.Functions.Like(device.SerialNumber, pattern, LikePatterns.Escape))
                || EF.Functions.Like(device.Customer!.FullName, pattern, LikePatterns.Escape));
        }

        var total = await matching.CountAsync(cancellationToken);
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)DeviceSearch.PageSize));
        var page = Math.Min(search.Page, lastPage);

        var items = await matching
            .OrderByDescending(device => device.CreatedAtUtc)
            .ThenByDescending(device => device.Id)
            .Skip((page - 1) * DeviceSearch.PageSize)
            .Take(DeviceSearch.PageSize)
            .Select(device => new DeviceListItem(
                device.Id,
                device.Type,
                device.Brand,
                device.Model,
                device.SerialNumber,
                device.CustomerId,
                device.Customer!.FullName,
                device.Customer.IsActive,
                db.ServiceOrders.Count(order => order.DeviceId == device.Id),
                device.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<DeviceListItem>(items, page, DeviceSearch.PageSize, total);
    }

    public async Task<DeviceDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken)
    {
        var details = await db.Devices
            .AsNoTracking()
            .Where(device => device.Id == id)
            .Select(device => new DeviceDetails(
                device.Id,
                device.Type,
                device.Brand,
                device.Model,
                device.SerialNumber,
                device.Notes,
                device.CreatedAtUtc,
                device.UpdatedAtUtc,
                new CustomerOption(
                    device.Customer!.Id,
                    device.Customer.FullName,
                    device.Customer.Phone,
                    device.Customer.Email,
                    device.Customer.IsActive),
                Array.Empty<ServiceOrderSummary>(),
                device.Version))
            .SingleOrDefaultAsync(cancellationToken);
        if (details is null)
        {
            return null;
        }

        var orders = await db.ServiceOrders
            .AsNoTracking()
            .Where(order => order.DeviceId == id)
            .OrderByDescending(order => order.ReceivedAtUtc)
            .ThenByDescending(order => order.Id)
            .Select(ServiceOrderQueries.Summary)
            .ToListAsync(cancellationToken);
        return details with { ServiceOrders = orders };
    }

    public async Task<IReadOnlyList<CustomerDeviceItem>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken) =>
        await db.Devices
            .AsNoTracking()
            .Where(device => device.CustomerId == customerId)
            .OrderByDescending(device => device.CreatedAtUtc)
            .ThenByDescending(device => device.Id)
            .Select(device => new CustomerDeviceItem(
                device.Id,
                device.Type,
                device.Brand,
                device.Model,
                device.SerialNumber,
                db.ServiceOrders.Count(order => order.DeviceId == device.Id),
                device.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    public Task<Device?> FindAsync(int id, CancellationToken cancellationToken) =>
        db.Devices.SingleOrDefaultAsync(device => device.Id == id, cancellationToken);

    public Task<Device?> FindWithOwnerAsync(int id, CancellationToken cancellationToken) =>
        db.Devices.Include(device => device.Customer).SingleOrDefaultAsync(device => device.Id == id, cancellationToken);

    // En SQL Server la comparación usa la intercalación de las columnas (sin distinguir mayúsculas),
    // igual que el índice único UX_Devices_Brand_SerialNumber.
    public Task<bool> SerialNumberExistsAsync(string brand, string serialNumber, int? excludingDeviceId, CancellationToken cancellationToken) =>
        db.Devices.AnyAsync(
            device => device.Brand == brand
                && device.SerialNumber == serialNumber
                && (excludingDeviceId == null || device.Id != excludingDeviceId),
            cancellationToken);

    public void Add(Device device) => db.Devices.Add(device);
}
