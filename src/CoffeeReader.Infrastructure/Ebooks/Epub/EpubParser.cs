using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AngleSharp.Dom;
using CoffeeReader.Infrastructure.Ebooks.Html;
using CoffeeReader.Application.Abstractions;
using CoffeeReader.Domain.Common;
using CoffeeReader.Domain.Entities;

namespace CoffeeReader.Infrastructure.Ebooks.Epub;

/// <summary>
/// Reads metadata, reading order, table of contents and cover from EPUB 2 and EPUB 3 files.
/// </summary>
public sealed partial class EpubParser : IEbookParser
{
    private const int MaxCoverBytes = 25 * 1024 * 1024;

    public BookFormat Format => BookFormat.Epub;

    public IReadOnlyCollection<string> Extensions { get; } = [".epub"];

    public Task<ParsedEbook> ParseAsync(string fullPath, CancellationToken cancellationToken) =>
        Task.Run(() => Parse(fullPath), cancellationToken);

    internal static ParsedEbook Parse(string fullPath)
    {
        try
        {
            using var package = EpubPackage.Open(fullPath);
            var metadata = package.Opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata")
                ?? new XElement("metadata");
            var refinements = ReadRefinements(metadata);

            var chapters = package.Spine
                .Select(s => new ParsedChapter(s.Item.Path, package.FindEntry(s.Item.Path)?.Length ?? 0, s.IsLinear))
                .ToList();
            var toc = ReadTableOfContents(package);
            if (toc.Count == 0)
            {
                toc = BuildFallbackToc(package);
            }

            var title = ReadTitle(metadata, refinements) ?? Path.GetFileNameWithoutExtension(fullPath);
            var (seriesName, seriesIndex) = ReadSeries(metadata, refinements);
            var (coverHref, coverImage) = ReadCover(package, metadata);

            return new ParsedEbook(
                new BookMetadata(
                    title,
                    ReadDescription(metadata),
                    Dc(metadata, "publisher"),
                    Dc(metadata, "language"),
                    ReadDate(metadata),
                    ReadIsbn(metadata),
                    ReadIdentifier(package, metadata)),
                ReadAuthors(metadata, refinements),
                seriesName,
                seriesIndex,
                chapters,
                toc,
                coverHref,
                coverImage);
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
            throw new EbookFormatException($"The EPUB couldn't be read: {ex.Message}", ex);
        }
    }

    // ---- Metadata -------------------------------------------------------------------------------------

    /// <summary>EPUB 3 "refines" metadata, keyed by the id of the element being refined.</summary>
    private static Dictionary<string, List<(string Property, string Value)>> ReadRefinements(XElement metadata)
    {
        var result = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);
        foreach (var meta in metadata.Elements().Where(e => e.Name.LocalName == "meta"))
        {
            var refines = ((string?)meta.Attribute("refines"))?.TrimStart('#');
            var property = (string?)meta.Attribute("property");
            if (string.IsNullOrEmpty(refines) || string.IsNullOrEmpty(property))
            {
                continue;
            }

            if (!result.TryGetValue(refines, out var list))
            {
                result[refines] = list = [];
            }

            list.Add((property, meta.Value.Trim()));
        }

        return result;
    }

    private static string? Refinement(Dictionary<string, List<(string Property, string Value)>> refinements, XElement element, string property)
    {
        var id = (string?)element.Attribute("id");
        if (id is null || !refinements.TryGetValue(id, out var list))
        {
            return null;
        }

        return list.FirstOrDefault(r => r.Property == property).Value;
    }

    private static string? Dc(XElement metadata, string name) =>
        metadata.Elements().Where(e => e.Name.LocalName == name).Select(e => Clean(e.Value)).FirstOrDefault(v => v is not null);

    private static string? ReadTitle(XElement metadata, Dictionary<string, List<(string Property, string Value)>> refinements)
    {
        var titles = metadata.Elements().Where(e => e.Name.LocalName == "title" && Clean(e.Value) is not null).ToList();
        var main = titles.FirstOrDefault(t => Refinement(refinements, t, "title-type") == "main") ?? titles.FirstOrDefault();
        return main is null ? null : Clean(main.Value);
    }

    private static List<ParsedAuthor> ReadAuthors(XElement metadata, Dictionary<string, List<(string Property, string Value)>> refinements)
    {
        var creators = metadata.Elements()
            .Where(e => e.Name.LocalName == "creator" && Clean(e.Value) is not null)
            .Select(e => new
            {
                Name = Clean(e.Value)!,
                Role = (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "role") ?? Refinement(refinements, e, "role"),
                FileAs = (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "file-as") ?? Refinement(refinements, e, "file-as"),
            })
            .ToList();

        var authors = creators.Where(c => c.Role is null || c.Role.Equals("aut", StringComparison.OrdinalIgnoreCase)).ToList();
        if (authors.Count == 0)
        {
            authors = creators;
        }

        var result = new List<ParsedAuthor>();
        foreach (var author in authors)
        {
            var names = SplitAuthors(author.Name);
            result.AddRange(names.Select(name => new ParsedAuthor(name, names.Count == 1 ? Clean(author.FileAs) : null)));
        }

        return result.Where(a => !a.Name.Equals("unknown", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Some tools put several authors in one creator element ("A & B").</summary>
    private static List<string> SplitAuthors(string value) =>
        AuthorSeparator().Split(value).Select(v => v.Trim()).Where(v => v.Length > 0).ToList();

    private static (string? Name, double? Index) ReadSeries(XElement metadata, Dictionary<string, List<(string Property, string Value)>> refinements)
    {
        // EPUB 3 collections
        foreach (var collection in metadata.Elements().Where(e => e.Name.LocalName == "meta" && (string?)e.Attribute("property") == "belongs-to-collection"))
        {
            var type = Refinement(refinements, collection, "collection-type");
            if (type is null or "series")
            {
                var name = Clean(collection.Value);
                if (name is not null)
                {
                    return (name, ParseNumber(Refinement(refinements, collection, "group-position")));
                }
            }
        }

        // Calibre's metadata, used by most EPUB 2 files in the wild
        string? Meta(string key) => metadata.Elements()
            .Where(e => e.Name.LocalName == "meta" && (string?)e.Attribute("name") == key)
            .Select(e => Clean((string?)e.Attribute("content")))
            .FirstOrDefault(v => v is not null);

        var calibreSeries = Meta("calibre:series");
        return calibreSeries is null ? (null, null) : (calibreSeries, ParseNumber(Meta("calibre:series_index")));
    }

    private static string? ReadDescription(XElement metadata)
    {
        var raw = metadata.Elements().Where(e => e.Name.LocalName == "description").Select(e => e.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (raw is null)
        {
            return null;
        }

        // Descriptions are often HTML (Calibre writes them that way); keep the paragraph breaks.
        var text = XhtmlLoader.ToPlainText(raw.Contains('<') ? raw : raw.Replace("\n", "<br>"));
        text = text.Trim();
        return text.Length == 0 ? null : text.Length > 10_000 ? text[..10_000] : text;
    }

    private static string? ReadDate(XElement metadata)
    {
        var dates = metadata.Elements().Where(e => e.Name.LocalName == "date").ToList();
        var publication = dates.FirstOrDefault(d => ((string?)d.Attributes().FirstOrDefault(a => a.Name.LocalName == "event"))?.Equals("publication", StringComparison.OrdinalIgnoreCase) == true)
            ?? dates.FirstOrDefault();
        var value = Clean(publication?.Value);
        if (value is null || value.StartsWith("0101", StringComparison.Ordinal))
        {
            return null;
        }

        // Keep the date part of full timestamps ("2010-05-01T00:00:00+00:00").
        return value.Length > 10 && value[10] == 'T' ? value[..10] : value;
    }

    private static string? ReadIsbn(XElement metadata)
    {
        foreach (var identifier in metadata.Elements().Where(e => e.Name.LocalName == "identifier"))
        {
            var value = Clean(identifier.Value);
            if (value is null)
            {
                continue;
            }

            var scheme = (string?)identifier.Attributes().FirstOrDefault(a => a.Name.LocalName == "scheme");
            var candidate = value.StartsWith("urn:isbn:", StringComparison.OrdinalIgnoreCase) ? value[9..]
                : value.StartsWith("isbn:", StringComparison.OrdinalIgnoreCase) ? value[5..]
                : value;
            var digits = new string(candidate.Where(c => char.IsDigit(c) || c is 'X' or 'x').ToArray());
            var looksLikeIsbn = digits.Length is 10 or 13 && candidate.All(c => char.IsDigit(c) || c is '-' or ' ' or 'X' or 'x');
            if (looksLikeIsbn || string.Equals(scheme, "ISBN", StringComparison.OrdinalIgnoreCase))
            {
                return digits.Length > 0 ? digits.ToUpperInvariant() : candidate;
            }
        }

        return null;
    }

    private static string? ReadIdentifier(EpubPackage package, XElement metadata)
    {
        var uniqueId = (string?)package.Opf.Root?.Attribute("unique-identifier");
        var identifiers = metadata.Elements().Where(e => e.Name.LocalName == "identifier").ToList();
        var primary = identifiers.FirstOrDefault(i => uniqueId is not null && (string?)i.Attribute("id") == uniqueId) ?? identifiers.FirstOrDefault();
        return Clean(primary?.Value);
    }

    // ---- Cover ----------------------------------------------------------------------------------------

    private static (string? Href, byte[]? Image) ReadCover(EpubPackage package, XElement metadata)
    {
        foreach (var candidate in CoverCandidates(package, metadata))
        {
            var entry = package.FindEntry(candidate);
            if (entry is null || entry.Length is 0 or > MaxCoverBytes)
            {
                continue;
            }

            var bytes = package.ReadBytes(candidate);
            if (bytes is { Length: > 0 })
            {
                return (candidate, bytes);
            }
        }

        return (null, null);
    }

    private static IEnumerable<string> CoverCandidates(EpubPackage package, XElement metadata)
    {
        // EPUB 3: <item properties="cover-image">
        foreach (var item in package.Manifest.Values.Where(m => m.HasProperty("cover-image")))
        {
            yield return item.Path;
        }

        // EPUB 2: <meta name="cover" content="item-id">
        var coverMeta = metadata.Elements()
            .Where(e => e.Name.LocalName == "meta" && string.Equals((string?)e.Attribute("name"), "cover", StringComparison.OrdinalIgnoreCase))
            .Select(e => (string?)e.Attribute("content"))
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (coverMeta is not null)
        {
            if (package.Manifest.TryGetValue(coverMeta, out var item) && item.IsImage)
            {
                yield return item.Path;
            }
            else
            {
                yield return ContainerPath.Resolve(package.OpfDirectory, coverMeta);
            }
        }

        // The first image on the cover page (from the guide, or the first page of the book)
        var guideCover = package.Opf.Descendants()
            .Where(e => e.Name.LocalName == "reference" && string.Equals((string?)e.Attribute("type"), "cover", StringComparison.OrdinalIgnoreCase))
            .Select(e => (string?)e.Attribute("href"))
            .FirstOrDefault(h => h is not null);
        var coverPages = new List<string>();
        if (guideCover is not null)
        {
            coverPages.Add(ContainerPath.Resolve(package.OpfDirectory, guideCover));
        }

        coverPages.Add(package.Spine[0].Item.Path);
        foreach (var page in coverPages.Distinct())
        {
            var image = FirstImageIn(package, page);
            if (image is not null)
            {
                yield return image;
            }
        }

        // Anything that looks like a cover by name
        foreach (var item in package.Manifest.Values.Where(m => m.IsImage && (m.Id.Contains("cover", StringComparison.OrdinalIgnoreCase) || m.Path.Contains("cover", StringComparison.OrdinalIgnoreCase))))
        {
            yield return item.Path;
        }
    }

    private static string? FirstImageIn(EpubPackage package, string documentPath)
    {
        var item = package.FindManifestItemByPath(documentPath);
        if (item is not null && item.IsImage)
        {
            return item.Path;
        }

        var text = package.ReadText(documentPath);
        if (text is null)
        {
            return null;
        }

        var document = XhtmlLoader.Parse(text);
        var source = document.QuerySelector("img[src]")?.GetAttribute("src")
            ?? document.All.Where(e => e.LocalName == "image")
                .Select(e => e.GetAttribute("xlink:href") ?? e.GetAttribute("href"))
                .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
        return source is null ? null : ContainerPath.Resolve(ContainerPath.DirectoryOf(documentPath), source);
    }

    // ---- Table of contents ----------------------------------------------------------------------------

    private static List<ParsedTocEntry> ReadTableOfContents(EpubPackage package)
    {
        var nav = package.Manifest.Values.FirstOrDefault(m => m.HasProperty("nav"));
        if (nav is not null)
        {
            var fromNav = ReadNavDocument(package, nav.Path);
            if (fromNav.Count > 0)
            {
                return fromNav;
            }
        }

        var ncx = (package.NcxId is not null && package.Manifest.TryGetValue(package.NcxId, out var ncxItem) ? ncxItem : null)
            ?? package.Manifest.Values.FirstOrDefault(m => m.MediaType == "application/x-dtbncx+xml");
        return ncx is null ? [] : ReadNcx(package, ncx.Path);
    }

    private static List<ParsedTocEntry> ReadNavDocument(EpubPackage package, string navPath)
    {
        var text = package.ReadText(navPath);
        if (text is null)
        {
            return [];
        }

        var document = XhtmlLoader.Parse(text);
        var navs = document.QuerySelectorAll("nav").ToList();
        var tocNav = navs.FirstOrDefault(n => (n.GetAttribute("epub:type") ?? n.GetAttribute("type") ?? string.Empty)
                         .Split(' ').Contains("toc"))
                     ?? navs.FirstOrDefault();
        var list = tocNav?.Children.FirstOrDefault(c => c.LocalName is "ol" or "ul");
        if (list is null)
        {
            return [];
        }

        var entries = new List<ParsedTocEntry>();
        var baseDirectory = ContainerPath.DirectoryOf(navPath);
        WalkNavList(package, list, 0, baseDirectory, entries);
        return entries;
    }

    private static void WalkNavList(EpubPackage package, IElement list, int depth, string baseDirectory, List<ParsedTocEntry> entries)
    {
        foreach (var item in list.Children.Where(c => c.LocalName == "li"))
        {
            var label = item.Children.FirstOrDefault(c => c.LocalName is "a" or "span");
            var title = Clean(label?.TextContent);
            var href = label?.LocalName == "a" ? label.GetAttribute("href") : null;
            var childList = item.Children.FirstOrDefault(c => c.LocalName is "ol" or "ul");

            if (title is not null)
            {
                var target = href is null ? FirstLinkIn(childList) : href;
                if (target is not null && Locate(package, baseDirectory, target) is { } location)
                {
                    entries.Add(new ParsedTocEntry(Truncate(title), depth, location.ChapterIndex, href is null ? null : location.Fragment));
                }
            }

            if (childList is not null)
            {
                WalkNavList(package, childList, title is null ? depth : depth + 1, baseDirectory, entries);
            }
        }
    }

    private static string? FirstLinkIn(IElement? list) =>
        list?.QuerySelector("a[href]")?.GetAttribute("href");

    private static List<ParsedTocEntry> ReadNcx(EpubPackage package, string ncxPath)
    {
        var document = package.ReadXml(ncxPath);
        var navMap = document?.Descendants().FirstOrDefault(e => e.Name.LocalName == "navMap");
        if (navMap is null)
        {
            return [];
        }

        var entries = new List<ParsedTocEntry>();
        WalkNavPoints(package, navMap, 0, ContainerPath.DirectoryOf(ncxPath), entries);
        return entries;
    }

    private static void WalkNavPoints(EpubPackage package, XElement parent, int depth, string baseDirectory, List<ParsedTocEntry> entries)
    {
        foreach (var point in parent.Elements().Where(e => e.Name.LocalName == "navPoint"))
        {
            var title = Clean(point.Elements().FirstOrDefault(e => e.Name.LocalName == "navLabel")?.Value);
            var src = (string?)point.Elements().FirstOrDefault(e => e.Name.LocalName == "content")?.Attribute("src");
            if (title is not null && src is not null && Locate(package, baseDirectory, src) is { } location)
            {
                entries.Add(new ParsedTocEntry(Truncate(title), depth, location.ChapterIndex, location.Fragment));
            }

            WalkNavPoints(package, point, depth + 1, baseDirectory, entries);
        }
    }

    private static (int ChapterIndex, string? Fragment)? Locate(EpubPackage package, string baseDirectory, string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https" or "mailto")
        {
            return null;
        }

        var path = ContainerPath.Resolve(baseDirectory, href);
        var (_, fragment) = ContainerPath.SplitFragment(href);
        for (var i = 0; i < package.Spine.Count; i++)
        {
            if (string.Equals(package.Spine[i].Item.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return (i, fragment);
            }
        }

        return null;
    }

    /// <summary>
    /// For books without a usable table of contents: one entry per document, named after its title or first heading.
    /// </summary>
    private static List<ParsedTocEntry> BuildFallbackToc(EpubPackage package)
    {
        var entries = new List<ParsedTocEntry>();
        for (var i = 0; i < package.Spine.Count; i++)
        {
            if (!package.Spine[i].IsLinear)
            {
                continue;
            }

            string? title = null;
            var text = package.ReadText(package.Spine[i].Item.Path);
            if (text is not null)
            {
                var document = XhtmlLoader.Parse(text);
                title = Clean(document.QuerySelector("h1, h2, h3")?.TextContent) ?? Clean(document.Title);
            }

            entries.Add(new ParsedTocEntry(Truncate(title ?? $"Section {entries.Count + 1}"), 0, i, null));
        }

        return entries;
    }

    // ---- Helpers --------------------------------------------------------------------------------------

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TextNormalization.CollapseWhitespace(value).Trim();
    }

    private static string Truncate(string value) => value.Length > 500 ? value[..500] : value;

    private static double? ParseNumber(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= 0
            ? number
            : null;

    [GeneratedRegex(@"\s*[&;]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorSeparator();
}
