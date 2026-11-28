using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace CoffeeReader.Infrastructure.Ebooks.Html;

/// <summary>
/// Parses EPUB content documents. They are XHTML, but often not well-formed enough for an XML parser,
/// so they're parsed as HTML after expanding XML-style self-closing tags (which HTML would misread).
/// </summary>
internal static partial class XhtmlLoader
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    private static readonly HtmlParser Parser = new(new HtmlParserOptions
    {
        IsKeepingSourceReferences = false,
        IsScripting = false,
    });

    public static IHtmlDocument Parse(string xhtml) => Parser.ParseDocument(ExpandSelfClosingTags(xhtml));

    public static INodeList ParseFragment(string html, IElement context) => Parser.ParseFragment(html, context);

    public static IHtmlDocument CreateEmptyDocument() => Parser.ParseDocument("<!DOCTYPE html><html><head></head><body></body></html>");

    internal static string ExpandSelfClosingTags(string xhtml)
    {
        // Drop the XML prolog; HTML would turn it into a stray comment.
        xhtml = XmlDeclaration().Replace(xhtml, string.Empty, 1);
        return SelfClosingTag().Replace(xhtml, match =>
        {
            var name = match.Groups["name"].Value;
            return VoidElements.Contains(name)
                ? match.Value
                : $"<{name}{match.Groups["attrs"].Value}></{name}>";
        });
    }

    /// <summary>
    /// Extracts readable text, keeping paragraph breaks.
    /// </summary>
    public static string ToPlainText(string html)
    {
        var document = Parser.ParseDocument($"<body>{html}</body>");
        var builder = new StringBuilder();
        AppendText(document.Body!, builder);
        var lines = builder.ToString().Split('\n').Select(l => Domain.Common.TextNormalization.CollapseWhitespace(l).Trim());
        var result = new StringBuilder();
        var blank = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blank++;
                continue;
            }

            if (result.Length > 0)
            {
                result.Append(blank > 0 ? "\n\n" : "\n");
            }

            result.Append(line);
            blank = 0;
        }

        return result.ToString();
    }

    private static void AppendText(INode node, StringBuilder builder)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    builder.Append(text.Data);
                    break;
                case IElement element when element.LocalName == "br":
                    builder.Append('\n');
                    break;
                case IElement element when element.LocalName is "script" or "style":
                    break;
                case IElement element:
                    var isBlock = element.LocalName is "p" or "div" or "li" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "blockquote" or "tr" or "section";
                    if (isBlock)
                    {
                        builder.Append("\n\n");
                    }

                    AppendText(element, builder);
                    if (isBlock)
                    {
                        builder.Append("\n\n");
                    }

                    break;
            }
        }
    }

    [GeneratedRegex(@"^\s*<\?xml[^>]*\?>", RegexOptions.CultureInvariant)]
    private static partial Regex XmlDeclaration();

    [GeneratedRegex(@"<(?<name>[A-Za-z][A-Za-z0-9:._-]*)(?<attrs>(?:\s+[^\s=/>]+(?:\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+))?)*)\s*/>", RegexOptions.CultureInvariant)]
    private static partial Regex SelfClosingTag();
}
