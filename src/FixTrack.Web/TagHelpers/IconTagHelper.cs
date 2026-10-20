using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FixTrack.Web.TagHelpers;

/// <summary>
/// Renderiza un ícono del sprite <c>wwwroot/icons/icons.svg</c>.
/// Uso: <c>&lt;icon name="wrench" /&gt;</c>. Los íconos son decorativos (<c>aria-hidden</c>);
/// el texto accesible debe estar en el elemento que los contiene.
/// </summary>
[HtmlTargetElement("icon", Attributes = "name", TagStructure = TagStructure.WithoutEndTag)]
public sealed class IconTagHelper(IFileVersionProvider fileVersionProvider) : TagHelper
{
    private const string SpritePath = "/icons/icons.svg";

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    /// <summary>Identificador del símbolo dentro del sprite.</summary>
    public string Name { get; set; } = string.Empty;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var pathBase = ViewContext.HttpContext.Request.PathBase;
        var spriteUrl = fileVersionProvider.AddFileVersionToPath(pathBase, pathBase + SpritePath);
        var href = HtmlEncoder.Default.Encode($"{spriteUrl}#{Name}");

        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.AddClass("icon", HtmlEncoder.Default);
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Attributes.SetAttribute("focusable", "false");
        output.Content.SetHtmlContent($"<use href=\"{href}\"></use>");
    }
}
