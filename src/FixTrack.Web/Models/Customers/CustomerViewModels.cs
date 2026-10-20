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
