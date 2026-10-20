using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Domain.Common;
using FixTrack.Domain.Devices;

namespace FixTrack.Application.Devices;

internal sealed class DeviceService(
    IDeviceRepository devices,
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IDeviceService
{
    internal const string StaleMessage =
        "Otra persona modificó este equipo mientras lo editabas. Revisa los datos actuales y vuelve a intentarlo.";

    internal const string CustomerField = "CustomerId";
    internal const string DuplicateSerialMessage = "Ya existe un equipo de esta marca con el mismo número de serie.";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public Task<PagedResult<DeviceListItem>> SearchAsync(DeviceSearch search, CancellationToken cancellationToken)
    {
        var text = Text.CleanOrNull(search.Text);
        var normalized = search with
        {
            Text = text is { Length: > DeviceSearch.TextMaxLength } ? text[..DeviceSearch.TextMaxLength] : text,
            Type = search.Type is { } type && Enum.IsDefined(type) ? type : null,
            Page = Math.Max(1, search.Page),
        };

        return devices.SearchAsync(normalized, cancellationToken);
    }

    public Task<DeviceDetails?> GetAsync(int id, CancellationToken cancellationToken) =>
        id > 0 ? devices.GetDetailsAsync(id, cancellationToken) : Task.FromResult<DeviceDetails?>(null);

    public Task<IReadOnlyList<CustomerDeviceItem>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken) =>
        devices.ListByCustomerAsync(customerId, cancellationToken);

    public async Task<CommandResult<int>> RegisterAsync(RegisterDeviceInput input, CancellationToken cancellationToken)
    {
        var data = Normalize(input.Details);
        var errors = new Dictionary<string, string>(Validate(data));

        var owner = input.CustomerId is > 0 ? await customers.FindAsync(input.CustomerId.Value, cancellationToken) : null;
        if (owner is null)
        {
            errors[CustomerField] = "Selecciona el cliente propietario del equipo.";
        }
        else if (!owner.IsActive)
        {
            errors[CustomerField] = "El cliente está inactivo. Reactívalo para registrarle equipos nuevos.";
        }

        await AddDuplicateSerialErrorAsync(errors, data, excludingDeviceId: null, cancellationToken);
        if (errors.Count > 0)
        {
            return CommandResult<int>.Invalid(errors);
        }

        var device = owner!.RegisterDevice(data.Type!.Value, data.Brand!, data.Model, data.SerialNumber, data.Notes, UtcNow);
        devices.Add(device);

        if (!await TrySaveAsync(cancellationToken))
        {
            return CommandResult<int>.Invalid(DuplicateSerial());
        }

        return CommandResult<int>.Success(device.Id);
    }

    public async Task<CommandResult> UpdateAsync(int id, DeviceInput input, int version, CancellationToken cancellationToken)
    {
        var device = id > 0 ? await devices.FindAsync(id, cancellationToken) : null;
        if (device is null)
        {
            return CommandResult.NotFound();
        }

        // Concurrencia optimista: si otra persona guardó después de abrir el formulario, no se sobrescribe su cambio.
        if (device.Version != version)
        {
            return CommandResult.Conflict(StaleMessage);
        }

        var data = Normalize(input);
        var errors = new Dictionary<string, string>(Validate(data));
        await AddDuplicateSerialErrorAsync(errors, data, excludingDeviceId: id, cancellationToken);
        if (errors.Count > 0)
        {
            return CommandResult.Invalid(errors);
        }

        device.UpdateDetails(data.Type!.Value, data.Brand!, data.Model, data.SerialNumber, data.Notes, UtcNow);
        try
        {
            return await TrySaveAsync(cancellationToken) ? CommandResult.Success() : CommandResult.Invalid(DuplicateSerial());
        }
        catch (ConcurrencyConflictException)
        {
            return CommandResult.Conflict(StaleMessage);
        }
    }

    private static DeviceInput Normalize(DeviceInput input) => new(
        input.Type,
        Text.Clean(input.Brand),
        Text.CleanOrNull(input.Model),
        DeviceRules.NormalizeSerialNumber(input.SerialNumber),
        Text.CleanMultilineOrNull(input.Notes));

    private static IReadOnlyDictionary<string, string> Validate(DeviceInput data) =>
        DeviceRules.Validate(data.Type, data.Brand!, data.Model, data.SerialNumber, data.Notes);

    private async Task AddDuplicateSerialErrorAsync(
        Dictionary<string, string> errors,
        DeviceInput data,
        int? excludingDeviceId,
        CancellationToken cancellationToken)
    {
        if (data.SerialNumber is null
            || errors.ContainsKey(nameof(Device.Brand))
            || errors.ContainsKey(nameof(Device.SerialNumber)))
        {
            return;
        }

        if (await devices.SerialNumberExistsAsync(data.Brand!, data.SerialNumber, excludingDeviceId, cancellationToken))
        {
            errors[nameof(Device.SerialNumber)] = DuplicateSerialMessage;
        }
    }

    /// <summary>Guarda los cambios. Devuelve <c>false</c> si otro equipo tomó el mismo número de serie entre la validación y el guardado.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DuplicateValueException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> DuplicateSerial() =>
        new() { [nameof(Device.SerialNumber)] = DuplicateSerialMessage };
}
