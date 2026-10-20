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
