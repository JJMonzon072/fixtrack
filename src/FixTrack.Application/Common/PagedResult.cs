namespace FixTrack.Application.Common;

/// <summary>Datos de paginación, independientes del tipo de elemento (los usa la vista de paginación).</summary>
public interface IPageInfo
{
    int Page { get; }

    int TotalCount { get; }

    int TotalPages { get; }

    bool HasPrevious { get; }

    bool HasNext { get; }

    int FirstItemNumber { get; }

    int LastItemNumber { get; }
}

/// <summary>Una página de resultados y los datos necesarios para paginar.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount) : IPageInfo
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    /// <summary>Posición (desde 1) del primer elemento de la página; 0 si no hay resultados.</summary>
    public int FirstItemNumber => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int LastItemNumber => Math.Min(Page * PageSize, TotalCount);
}
