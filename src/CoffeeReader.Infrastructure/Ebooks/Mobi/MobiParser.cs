using CoffeeReader.Application.Abstractions;
using CoffeeReader.Domain.Common;
using CoffeeReader.Domain.Entities;
using CoffeeReader.Infrastructure.Ebooks.Html;

namespace CoffeeReader.Infrastructure.Ebooks.Mobi;

/// <summary>
/// Reads metadata, structure and cover from MOBI books (Mobipocket and Kindle MOBI 6, including the
/// MOBI 6 half of KindleGen's combined files).
/// </summary>
public sealed class MobiParser : IEbookParser
{
    public BookFormat Format => BookFormat.Mobi;

    public IReadOnlyCollection<string> Extensions { get; } = [".mobi", ".azw", ".prc"];

    /// <summary>1: "&amp;" no longer splits names like "Black &amp; Decker" into two authors.</summary>
    public int MetadataVersion => 1;

    public Task<ParsedEbook> ParseAsync(string fullPath, CancellationToken cancellationToken) =>
        Task.Run(() => Parse(fullPath), cancellationToken);

    internal static ParsedEbook Parse(string fullPath)
    {
        try
        {
            using var database = PalmDatabase.Open(fullPath);
            var header = MobiBook.ReadHeader(database);
            var book = MobiBook.Load(fullPath);

            var chapters = book.Chapters.Select(c => new ParsedChapter(c.Href, c.Html.Length, true)).ToList();
            var toc = ReadTableOfContents(book);
            var (coverHref, cover) = ReadCover(database, header);

            var title = header.ExthString(Exth.UpdatedTitle) ?? header.FullName ?? Path.GetFileNameWithoutExtension(fullPath);
            var description = header.ExthString(Exth.Description);
            if (description is not null && description.Contains('<'))
            {
                description = XhtmlLoader.ToPlainText(description);
            }

            var authors = header.ExthStrings(Exth.Author)
                .SelectMany(AuthorNames.Split)
                .Where(a => !a.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                .Select(a => new ParsedAuthor(a, a.Contains(',') ? a : null))
                .ToList();

            var date = header.ExthString(Exth.PublishedDate);
            return new ParsedEbook(
                new BookMetadata(
                    TextNormalization.CollapseWhitespace(title),
                    description,
                    header.ExthString(Exth.Publisher),
                    header.ExthString(Exth.Language),
                    date is { Length: > 10 } && date[10] == 'T' ? date[..10] : date,
                    header.ExthString(Exth.Isbn)?.Replace("-", string.Empty),
                    null),
                authors,
                null,
                null,
                chapters,
                toc,
                coverHref,
                cover);
        }
        catch (EbookFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EbookFormatException($"The MOBI book couldn't be read: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Uses the book's own contents page when it has one; otherwise one entry per section, named after
    /// its first heading.
    /// </summary>
    private static List<ParsedTocEntry> ReadTableOfContents(MobiBook book)
    {
        var entries = new List<ParsedTocEntry>();
        if (book.TocChapter is { } tocChapter)
        {
            var document = XhtmlLoader.Parse(book.Chapters[tocChapter].Html);
            var links = document.QuerySelectorAll("a[href]")
                .Select(a => (Element: a, Href: a.GetAttribute("href")!))
                .Where(l => l.Href.Contains("#filepos", StringComparison.Ordinal))
                .ToList();
            var depths = links.Select(l => ListDepth(l.Element)).ToList();
            var minimum = depths.Count == 0 ? 0 : depths.Min();
            for (var i = 0; i < links.Count; i++)
            {
                var title = TextNormalization.CollapseWhitespace(links[i].Element.TextContent).Trim();
                var (path, fragment) = ContainerPath.SplitFragment(links[i].Href);
                var chapterIndex = IndexOfChapter(book, ContainerPath.Resolve(MobiBook.ChapterDirectory, path));
                if (title.Length > 0 && chapterIndex >= 0 && chapterIndex != tocChapter)
                {
                    entries.Add(new ParsedTocEntry(title.Length > 500 ? title[..500] : title, depths[i] - minimum, chapterIndex, fragment));
                }
            }
        }

        if (entries.Count > 0)
        {
            return entries;
        }

        for (var i = 0; i < book.Chapters.Count; i++)
        {
            var heading = XhtmlLoader.Parse(book.Chapters[i].Html).QuerySelector("h1, h2, h3")?.TextContent;
            var title = heading is null ? null : TextNormalization.CollapseWhitespace(heading).Trim();
            entries.Add(new ParsedTocEntry(string.IsNullOrEmpty(title) ? $"Section {i + 1}" : title, 0, i, null));
        }

        return entries;
    }

    private static int ListDepth(AngleSharp.Dom.IElement element)
    {
        var depth = 0;
        for (var parent = element.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            if (parent.LocalName is "ul" or "ol" or "blockquote" or "dl")
            {
                depth++;
            }
        }

        return depth;
    }

    private static int IndexOfChapter(MobiBook book, string href)
    {
        for (var i = 0; i < book.Chapters.Count; i++)
        {
            if (string.Equals(book.Chapters[i].Href, href, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static (string? Href, byte[]? Image) ReadCover(PalmDatabase database, MobiHeader header)
    {
        foreach (var offset in new[] { header.ExthNumber(Exth.CoverOffset), header.ExthNumber(Exth.ThumbnailOffset), 0 })
        {
            if (offset is { } value && value >= 0 && value < 0x00FFFFFF)
            {
                var image = MobiBook.ReadImage(database, header, value + 1);
                if (image is not null)
                {
                    return (MobiBook.ImagePath(value + 1), image);
                }
            }
        }

        return (null, null);
    }
}
