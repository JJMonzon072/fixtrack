using FixTrack.Application.Common;
using FixTrack.Domain.Devices;

namespace FixTrack.Application.Devices;

/// <summary>Acceso a los equipos. Lo implementa la capa de infraestructura.</summary>
public interface IDeviceRepository
{
    Task<PagedResult<DeviceListItem>> SearchAsync(DeviceSearch search, CancellationToken cancellationToken);

    Task<DeviceDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken);

    /// <summary>Equipos de un cliente, con la cantidad de órdenes de cada uno, en una sola consulta.</summary>
    Task<IReadOnlyList<CustomerDeviceItem>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>Obtiene el equipo para modificarlo.</summary>
    Task<Device?> FindAsync(int id, CancellationToken cancellationToken);

    /// <summary>Obtiene el equipo junto con su propietario (por ejemplo, para registrarle una orden).</summary>
    Task<Device?> FindWithOwnerAsync(int id, CancellationToken cancellationToken);

    /// <summary>Indica si otra unidad de la misma marca ya usa ese número de serie.</summary>
    Task<bool> SerialNumberExistsAsync(string brand, string serialNumber, int? excludingDeviceId, CancellationToken cancellationToken);

    void Add(Device device);
}
