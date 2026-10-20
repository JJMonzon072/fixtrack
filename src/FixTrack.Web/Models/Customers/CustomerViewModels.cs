using FixTrack.Application.Common;
using FixTrack.Application.Customers;

namespace FixTrack.Web.Models.Customers;

/// <summary>Formulario de registro o edición. <see cref="CustomerId"/> es null al registrar.</summary>
public sealed record CustomerEditorViewModel(int? CustomerId, string? CustomerName, CustomerFormModel Form)
{
    public bool IsNew => CustomerId is null;
}

public sealed record CustomerIndexViewModel(string? SearchText, StatusFilter Status, CustomerSearchResult Result)
{
    public bool HasSearch => !string.IsNullOrEmpty(SearchText);

    /// <summary>No existe ningún cliente registrado todavía.</summary>
    public bool IsFirstUse => !HasSearch && Result.ActiveCount + Result.InactiveCount == 0;

    public static string StatusParameter(StatusFilter status) => status switch
    {
        StatusFilter.Inactive => "inactivos",
        StatusFilter.All => "todos",
        _ => "activos",
    };

    public static StatusFilter ParseStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "inactivos" => StatusFilter.Inactive,
        "todos" => StatusFilter.All,
        _ => StatusFilter.Active,
    };
}

public static class CustomerDisplay
{
    /// <summary>Iniciales para el avatar: primera letra de las dos primeras palabras.</summary>
    public static string Initials(string fullName)
    {
        var letters = fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.FirstOrDefault(char.IsLetterOrDigit))
            .Where(letter => letter != default)
            .Take(2)
            .Select(char.ToUpperInvariant);
        return string.Concat(letters);
    }

    /// <summary>Número para enlaces tel: (solo dígitos y '+').</summary>
    public static string PhoneHref(string phone) =>
        "tel:" + string.Concat(phone.Where(c => char.IsAsciiDigit(c) || c == '+'));
}
