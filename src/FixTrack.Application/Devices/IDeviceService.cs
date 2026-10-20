using FixTrack.Application.Common;

namespace FixTrack.Application.Devices;

/// <summary>Casos de uso del módulo Equipos.</summary>
public interface IDeviceService
{
    Task<PagedResult<DeviceListItem>> SearchAsync(DeviceSearch search, CancellationToken cancellationToken);

    Task<DeviceDetails?> GetAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerDeviceItem>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>Registra un equipo para un cliente activo. Si tiene éxito, devuelve su Id.</summary>
    Task<CommandResult<int>> RegisterAsync(RegisterDeviceInput input, CancellationToken cancellationToken);

    /// <summary>Actualiza los datos técnicos. El propietario no se modifica.</summary>
    /// <param name="version">Versión que vio quien edita: si cambió, se devuelve un conflicto sin guardar.</param>
    Task<CommandResult> UpdateAsync(int id, DeviceInput input, int version, CancellationToken cancellationToken);
}
