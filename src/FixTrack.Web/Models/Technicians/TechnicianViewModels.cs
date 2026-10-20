using FixTrack.Application.Common;
using FixTrack.Application.Technicians;

namespace FixTrack.Web.Models.Technicians;

/// <summary>Campos del formulario de técnico. El estado solo cambia con las acciones de desactivar y reactivar.</summary>
public sealed class TechnicianFormModel
{
    public string? FullName { get; set; }

    public string? Specialty { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    /// <summary>Versión del registro cuando se abrió el formulario (concurrencia optimista).</summary>
    public int Version { get; set; }

    public TechnicianInput ToInput() => new(FullName, Specialty, Phone, Email);

    public static TechnicianFormModel From(TechnicianDetails technician) => new()
    {
        Version = technician.Version,
        FullName = technician.FullName,
        Specialty = technician.Specialty,
        Phone = technician.Phone,
        Email = technician.Email,
    };
}

/// <summary>Formulario de registro o edición. <see cref="TechnicianId"/> es null al registrar.</summary>
public sealed record TechnicianEditorViewModel(int? TechnicianId, string? TechnicianName, TechnicianFormModel Form)
{
    public bool IsNew => TechnicianId is null;
}

public sealed record TechnicianIndexViewModel(string? SearchText, StatusFilter Status, TechnicianSearchResult Result)
{
    public bool HasSearch => !string.IsNullOrEmpty(SearchText);

    /// <summary>No existe ningún técnico registrado todavía.</summary>
    public bool IsFirstUse => !HasSearch && Result.ActiveCount + Result.InactiveCount == 0;
}
