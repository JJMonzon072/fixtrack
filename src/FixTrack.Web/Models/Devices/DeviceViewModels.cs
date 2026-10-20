using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Domain.Devices;

namespace FixTrack.Web.Models.Devices;

/// <summary>
/// Campos del formulario de equipo. <see cref="CustomerId"/> solo se usa al registrar: al editar se ignora,
/// porque el propietario de un equipo no puede cambiarse.
/// </summary>
public sealed class DeviceFormModel
{
    public int? CustomerId { get; set; }

    public DeviceType? Type { get; set; }

    public string? Brand { get; set; }

    public string? Model { get; set; }

    public string? SerialNumber { get; set; }

    public string? Notes { get; set; }

    public DeviceInput ToInput() => new(Type, Brand, Model, SerialNumber, Notes);

    public static DeviceFormModel From(DeviceDetails device) => new()
    {
        CustomerId = device.Owner.Id,
        Type = device.Type,
        Brand = device.Brand,
        Model = device.Model,
        SerialNumber = device.SerialNumber,
        Notes = device.Notes,
    };
}

/// <summary>Formulario de registro o edición. <see cref="DeviceId"/> es null al registrar.</summary>
/// <param name="Owner">Propietario seleccionado (registro) o actual (edición).</param>
/// <param name="OwnerNotice">Aviso cuando el cliente indicado en la dirección no admite equipos nuevos.</param>
public sealed record DeviceEditorViewModel(
    int? DeviceId,
    string? DeviceTitle,
    DeviceFormModel Form,
    CustomerOption? Owner,
    string? OwnerNotice = null)
{
    public bool IsNew => DeviceId is null;
}

public sealed record DeviceIndexViewModel(string? SearchText, DeviceType? Type, PagedResult<DeviceListItem> Result)
{
    public bool HasFilters => !string.IsNullOrEmpty(SearchText) || Type is not null;

    /// <summary>No existe ningún equipo registrado todavía.</summary>
    public bool IsFirstUse => !HasFilters && Result.TotalCount == 0;
}
