using FixTrack.Application.Customers;

namespace FixTrack.Web.Models.Customers;

/// <summary>
/// Campos del formulario de cliente. Las propiedades admiten null: la validación la realiza
/// la capa de aplicación en el servidor, con mensajes en español.
/// </summary>
public sealed class CustomerFormModel
{
    public string? FullName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Notes { get; set; }

    /// <summary>Versión del registro cuando se abrió el formulario (concurrencia optimista).</summary>
    public int Version { get; set; }

    public CustomerInput ToInput() => new(FullName, Phone, Email, Notes);

    public static CustomerFormModel From(CustomerDetails customer) => new()
    {
        Version = customer.Version,
        FullName = customer.FullName,
        Phone = customer.Phone,
        Email = customer.Email,
        Notes = customer.Notes,
    };
}
