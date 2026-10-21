using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace FixTrack.Web.TagHelpers;

/// <summary>
/// Ícono del sprite de los extras de portafolio (<c>wwwroot/icons/portfolio-icons.svg</c>), con el mismo marcado que
/// <see cref="IconTagHelper"/>. Uso: <c>&lt;portfolio-icon name="chart-line" /&gt;</c>. Un sprite aparte deja intacto
/// el principal y, con él, el HTML de las pantallas de la versión académica.
/// </summary>
[HtmlTargetElement("portfolio-icon", Attributes = "name", TagStructure = TagStructure.WithoutEndTag)]
public sealed class PortfolioIconTagHelper(IFileVersionProvider fileVersionProvider) : TagHelper
{
    private const string SpritePath = "/icons/portfolio-icons.svg";

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

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
