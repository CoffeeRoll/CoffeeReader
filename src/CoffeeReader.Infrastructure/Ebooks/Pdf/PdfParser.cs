using System.Globalization;
using System.Text.RegularExpressions;
using CoffeeReader.Application.Abstractions;
using CoffeeReader.Domain.Entities;

namespace CoffeeReader.Infrastructure.Ebooks.Pdf;

/// <summary>
/// Reads PDF metadata, the outline (as a table of contents) and renders the first page as the cover.
/// Each page is one "chapter" in CoffeeReader's model, so progress and search work per page.
/// </summary>
public sealed partial class PdfParser : IEbookParser
{
    public const string PagePrefix = "page/";

    public BookFormat Format => BookFormat.Pdf;

    public IReadOnlyCollection<string> Extensions { get; } = [".pdf"];

    /// <summary>1: "&amp;" no longer splits names like "Black &amp; Decker" into two authors.</summary>
    public int MetadataVersion => 1;

    public Task<ParsedEbook> ParseAsync(string fullPath, CancellationToken cancellationToken) =>
        Task.Run(() => Parse(fullPath), cancellationToken);

    public static string PageHref(int index) => $"{PagePrefix}{index + 1}";

    internal static ParsedEbook Parse(string fullPath) => Pdfium.Use(() =>
    {
        using var document = PdfDocument.Open(fullPath);
        var pages = document.PageCount;
        if (pages <= 0)
        {
            throw new EbookFormatException("The PDF has no pages.");
        }

        var title = document.MetaText("Title");
        if (title is null || LooksLikeSoftwareTitle(title))
        {
            title = Path.GetFileNameWithoutExtension(fullPath).Replace('_', ' ');
        }

        var authors = AuthorNames.Split(document.MetaText("Author"))
            .Where(a => a.Length < 200)
            .Select(a => new ParsedAuthor(a, null))
            .ToList();

        var chapters = Enumerable.Range(0, pages).Select(i => new ParsedChapter(PageHref(i), 1, true)).ToList();
        var toc = document.Outline()
            .Where(e => e.PageIndex < pages)
            .Select(e => new ParsedTocEntry(e.Title, e.Depth, e.PageIndex, null))
            .ToList();
        var cover = document.RenderPage(0, 600, 1200);

        return new ParsedEbook(
            new BookMetadata(title, document.MetaText("Subject"), null, null, ParseDate(document.MetaText("CreationDate")), null, null),
            authors,
            null,
            null,
            chapters,
            toc,
            cover is null ? null : PageHref(0),
            cover);
    });

    /// <summary>Office tools often store titles like "Microsoft Word - draft3.docx"; the file name is better.</summary>
    private static bool LooksLikeSoftwareTitle(string title) =>
        title.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase)
        || FileNameTitle().IsMatch(title)
        || title.Equals("untitled", StringComparison.OrdinalIgnoreCase);

    /// <summary>PDF dates look like "D:20200131120000+01'00'".</summary>
    private static string? ParseDate(string? value)
    {
        var match = value is null ? null : PdfDate().Match(value);
        if (match is not { Success: true })
        {
            return null;
        }

        var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        if (year < 1000)
        {
            return null;
        }

        return match.Groups[2].Success && match.Groups[3].Success
            ? $"{year:D4}-{match.Groups[2].Value}-{match.Groups[3].Value}"
            : $"{year:D4}";
    }

    [GeneratedRegex(@"\.(docx?|pdf|indd|pages|odt|rtf|txt|tex|qxp)$", RegexOptions.IgnoreCase)]
    private static partial Regex FileNameTitle();

    [GeneratedRegex(@"^(?:D:)?(\d{4})(\d{2})?(\d{2})?")]
    private static partial Regex PdfDate();
}
