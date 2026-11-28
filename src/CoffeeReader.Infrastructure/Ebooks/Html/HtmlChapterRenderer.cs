using System.Text;
using AngleSharp.Dom;
using CoffeeReader.Application.Abstractions;

namespace CoffeeReader.Infrastructure.Ebooks.Html;

/// <summary>
/// Turns an (X)HTML content document into a sanitized, render-ready chapter. Shared by every
/// format whose content is HTML (EPUB, MOBI).
/// </summary>
public static class HtmlChapterRenderer
{
    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "aside", "header", "footer", "blockquote", "li", "ul", "ol", "dl", "dt", "dd",
        "h1", "h2", "h3", "h4", "h5", "h6", "table", "tr", "td", "th", "pre", "figure", "figcaption", "hr", "br", "nav",
    };

    public static ChapterText ExtractText(string bodyHtml)
    {
        var document = XhtmlLoader.CreateEmptyDocument();
        document.Body!.InnerHtml = bodyHtml;
        var builder = new StringBuilder();
        var breaks = new List<int>();

        void Mark()
        {
            if (breaks.Count == 0 || breaks[^1] != builder.Length)
            {
                breaks.Add(builder.Length);
            }
        }

        void Walk(INode node)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is IText text)
                {
                    builder.Append(text.Data);
                }
                else if (child is IElement element)
                {
                    var block = BlockElements.Contains(element.LocalName);
                    if (block)
                    {
                        Mark();
                    }

                    Walk(element);
                    if (block)
                    {
                        Mark();
                    }
                }
            }
        }

        Walk(document.Body);
        return new ChapterText(builder.ToString(), breaks);
    }

    /// <summary>The text a browser would produce for the chapter body (its <c>textContent</c>).</summary>
    public static string PlainText(string bodyHtml) => ExtractText(bodyHtml).Text;

    public static ChapterDocument Render(string source, string chapterHref)
    {
        var document = XhtmlLoader.Parse(source);
        var directory = ContainerPath.DirectoryOf(chapterHref);

        var stylesheets = document.QuerySelectorAll("link[href]")
            .Where(l => (l.GetAttribute("rel") ?? string.Empty).Split(' ').Contains("stylesheet", StringComparer.OrdinalIgnoreCase))
            .Select(l => ContainerPath.Resolve(directory, l.GetAttribute("href")!))
            .Where(p => p.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        var inlineStyles = document.QuerySelectorAll("style")
            .Where(s => !ChapterSanitizer.IsInside(s, "svg"))
            .Select(s => ChapterSanitizer.SanitizeCss(s.TextContent))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var html = document.DocumentElement;
        var body = document.Body;
        var language = body?.GetAttribute("xml:lang") ?? body?.GetAttribute("lang")
            ?? html?.GetAttribute("xml:lang") ?? html?.GetAttribute("lang");
        var direction = body?.GetAttribute("dir") ?? html?.GetAttribute("dir");
        var bodyId = body?.GetAttribute("id");
        var bodyClass = body?.GetAttribute("class");
        var bodyStyle = ChapterSanitizer.SanitizeInlineStyle(body?.GetAttribute("style"));

        ChapterSanitizer.Sanitize(document);

        return new ChapterDocument(
            chapterHref,
            document.Body?.InnerHtml ?? string.Empty,
            stylesheets,
            inlineStyles,
            Attribute(bodyId),
            Attribute(bodyClass),
            bodyStyle,
            Attribute(language),
            direction is "rtl" or "ltr" ? direction : null);
    }

    private static string? Attribute(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 256 ? null : value.Trim();
}
