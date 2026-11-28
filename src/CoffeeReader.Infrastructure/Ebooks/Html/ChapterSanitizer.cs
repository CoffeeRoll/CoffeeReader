using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Ganss.Xss;

namespace CoffeeReader.Infrastructure.Ebooks.Html;

/// <summary>
/// Strips anything executable from book content while keeping the markup books actually use
/// (including inline SVG covers, MathML and EPUB semantic attributes).
/// </summary>
internal static class ChapterSanitizer
{
    private static readonly string[] ExtraTags =
    [
        // Sectioning and inline semantics
        "section", "article", "aside", "header", "footer", "nav", "main", "figure", "figcaption", "mark", "time",
        "ruby", "rt", "rp", "rb", "bdi", "bdo", "wbr", "details", "summary", "hgroup",
        // SVG (common for full-page cover images)
        "svg", "g", "image", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "text", "tspan",
        "defs", "lineargradient", "radialgradient", "stop", "clippath", "symbol", "use", "title", "desc",
        // MathML
        "math", "mi", "mo", "mn", "ms", "mtext", "mrow", "msup", "msub", "msubsup", "mfrac", "msqrt", "mroot",
        "mtable", "mtr", "mtd", "mover", "munder", "munderover", "mspace", "mstyle", "mpadded", "menclose",
        "semantics", "annotation", "mfenced",
    ];

    private static readonly string[] ExtraAttributes =
    [
        "id", "class", "style", "lang", "xml:lang", "dir", "title", "epub:type", "role", "aria-label", "aria-hidden",
        "colspan", "rowspan", "headers", "scope", "start", "reversed", "value", "type",
        "width", "height", "alt", "src", "href", "srcset", "sizes",
        // SVG
        "xmlns", "xmlns:xlink", "xlink:href", "viewbox", "preserveaspectratio", "x", "y", "x1", "y1", "x2", "y2",
        "cx", "cy", "r", "rx", "ry", "d", "points", "transform", "fill", "fill-opacity", "fill-rule", "stroke",
        "stroke-width", "stroke-opacity", "opacity", "offset", "stop-color", "font-size", "font-family", "text-anchor",
        "version",
        // MathML
        "display", "mathvariant", "displaystyle", "scriptlevel", "linethickness", "encoding", "accent", "stretchy",
        "fence", "separator", "lspace", "rspace", "columnalign", "notation",
    ];

    private static readonly string[] RemoveEntirely = ["script", "noscript", "style", "template", "iframe", "object", "embed", "form", "base", "meta", "link"];

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static void Sanitize(IHtmlDocument document)
    {
        foreach (var element in document.QuerySelectorAll(string.Join(',', RemoveEntirely)).ToList())
        {
            element.Remove();
        }

        if (document.Body is IHtmlElement body)
        {
            Sanitizer.SanitizeDom(document, body);
        }
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer
        {
            KeepChildNodes = true,
        };

        foreach (var tag in ExtraTags)
        {
            sanitizer.AllowedTags.Add(tag);
        }

        foreach (var attribute in ExtraAttributes)
        {
            sanitizer.AllowedAttributes.Add(attribute);
        }

        sanitizer.UriAttributes.Add("xlink:href");
        sanitizer.AllowedSchemes.Add("mailto");

        // Book links can be relative ("chapter2.xhtml#note3"); never make them absolute.
        sanitizer.FilterUrl += (_, e) =>
        {
            if (e.OriginalUrl.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                || e.OriginalUrl.TrimStart().StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase))
            {
                e.SanitizedUrl = null;
            }
        };

        return sanitizer;
    }

    /// <summary>
    /// Makes stylesheet text safe to embed in a &lt;style&gt; element.
    /// </summary>
    public static string SanitizeCss(string css) =>
        css.Replace("</", "<\\/", StringComparison.Ordinal)
            .Replace("expression(", "x(", StringComparison.OrdinalIgnoreCase)
            .Replace("javascript:", "x:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Keeps simple presentational declarations from a body's style attribute (e.g. centring a cover);
    /// anything that could load or run something is dropped.
    /// </summary>
    public static string? SanitizeInlineStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style) || style.Length > 1000)
        {
            return null;
        }

        var safe = style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(declaration => declaration.Contains(':')
                && !declaration.Contains("url(", StringComparison.OrdinalIgnoreCase)
                && !declaration.Contains("expression", StringComparison.OrdinalIgnoreCase)
                && !declaration.Contains("javascript", StringComparison.OrdinalIgnoreCase)
                && !declaration.Contains("behavior", StringComparison.OrdinalIgnoreCase)
                && !declaration.Contains('<')
                && !declaration.Contains('"')
                && !declaration.Contains('\\'))
            .ToList();
        return safe.Count == 0 ? null : string.Join("; ", safe);
    }

    public static bool IsInside(IElement element, string tagName)
    {
        for (var current = element.ParentElement; current is not null; current = current.ParentElement)
        {
            if (current.LocalName == tagName)
            {
                return true;
            }
        }

        return false;
    }
}
