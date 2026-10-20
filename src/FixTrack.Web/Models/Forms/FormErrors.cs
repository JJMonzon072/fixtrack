using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FixTrack.Web.Models.Forms;

/// <summary>Errores de validación de un formulario, en el orden en que aparecen los campos.</summary>
public sealed class FormErrors
{
    private readonly Dictionary<string, string> _byField;

    private FormErrors(IReadOnlyList<FormError> items)
    {
        Items = items;
        _byField = items.ToDictionary(item => item.Field, item => item.Message);
    }

    public IReadOnlyList<FormError> Items { get; }

    public int Count => Items.Count;

    public string? For(string field) => _byField.GetValueOrDefault(field);

    /// <param name="modelState">Estado de validación de la solicitud.</param>
    /// <param name="fields">Campos en orden de aparición: nombre, etiqueta e id del control al que lleva el enlace.</param>
    public static FormErrors From(ModelStateDictionary modelState, params (string Name, string Label, string? Anchor)[] fields)
    {
        var order = fields.Select(field => field.Name).ToList();
        var items = modelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .Select(entry =>
            {
                var field = fields.FirstOrDefault(f => f.Name == entry.Key);
                return new FormError(
                    entry.Key,
                    field.Label ?? entry.Key,
                    field.Anchor ?? entry.Key,
                    entry.Value!.Errors[0].ErrorMessage);
            })
            .OrderBy(error => order.IndexOf(error.Field) is var index and >= 0 ? index : int.MaxValue)
            .ToList();

        return new FormErrors(items);
    }
}

public sealed record FormError(string Field, string Label, string Anchor, string Message);
