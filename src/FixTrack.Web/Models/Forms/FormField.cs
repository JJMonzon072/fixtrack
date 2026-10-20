namespace FixTrack.Web.Models.Forms;

/// <summary>
/// Descripción de un campo de formulario. La vista parcial <c>_FormField</c> lo dibuja siempre igual:
/// etiqueta, ayuda, control y error, enlazados con aria-describedby y aria-invalid.
/// </summary>
public sealed record FormField(string Name, string Label)
{
    public string? Value { get; init; }

    public string? Hint { get; init; }

    public string? Error { get; init; }

    public bool Optional { get; init; }

    /// <summary>Tipo del input (text, tel, email…). Se ignora en áreas de texto y listas.</summary>
    public string InputType { get; init; } = "text";

    public int? MaxLength { get; init; }

    public string? Autocomplete { get; init; }

    public string? InputMode { get; init; }

    public bool Spellcheck { get; init; } = true;

    /// <summary>El campo ocupa las dos columnas del formulario.</summary>
    public bool Wide { get; init; }

    /// <summary>Usa la tipografía monoespaciada (teléfonos, números de serie).</summary>
    public bool Mono { get; init; }

    /// <summary>Texto fijo antes del valor, por ejemplo "Q" en los montos.</summary>
    public string? Prefix { get; init; }

    /// <summary>Si tiene valor, el campo es un área de texto con esa cantidad de filas y contador de caracteres.</summary>
    public int? Rows { get; init; }

    /// <summary>Si tiene valor, el campo es una lista desplegable con estas opciones.</summary>
    public IReadOnlyList<(string Value, string Text)>? Options { get; init; }

    /// <summary>Texto de la opción vacía de una lista desplegable.</summary>
    public string? Placeholder { get; init; }

    public string HintId => $"{Name}-hint";

    public string ErrorId => $"{Name}-error";

    public string? DescribedBy =>
        string.Join(' ', new[] { Hint is null ? null : HintId, Error is null ? null : ErrorId }.Where(id => id is not null))
            is { Length: > 0 } ids ? ids : null;
}
