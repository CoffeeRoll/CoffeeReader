using System.Text;
using System.Text.RegularExpressions;
using CoffeeReader.Application.Abstractions;
using CoffeeReader.Infrastructure.Ebooks.Html;

namespace CoffeeReader.Infrastructure.Ebooks.Mobi;

/// <summary>
/// A MOBI (Mobipocket / Kindle MOBI 6) book converted into HTML chapters the web reader can paginate.
/// </summary>
/// <remarks>
/// MOBI text is one long HTML-like stream. Chapters are split at <c>&lt;mbp:pagebreak/&gt;</c>;
/// <c>filepos</c> links (byte offsets into the text) become ordinary anchors; images referenced by
/// <c>recindex</c> become resource paths served from the book's image records.
/// </remarks>
internal sealed partial class MobiBook
{
    public const string ChapterDirectory = "text";
    public const string ImageDirectory = "images";
    private const int MaxChapterChars = 256 * 1024;

    private MobiBook(MobiHeader header, IReadOnlyList<MobiChapter> chapters, int? tocChapter)
    {
        Header = header;
        Chapters = chapters;
        TocChapter = tocChapter;
    }

    public MobiHeader Header { get; }

    public IReadOnlyList<MobiChapter> Chapters { get; }

    /// <summary>The chapter holding the book's own table of contents page, if it has one.</summary>
    public int? TocChapter { get; }

    public long Size => Chapters.Sum(c => (long)c.Html.Length * sizeof(char));

    public static MobiBook Load(string fullPath)
    {
        using var database = PalmDatabase.Open(fullPath);
        var header = ReadHeader(database);
        var raw = DecompressText(database, header);
        return Convert(header, raw);
    }

    public static MobiHeader ReadHeader(PalmDatabase database)
    {
        if (!string.Equals(database.Creator, "MOBI", StringComparison.Ordinal))
        {
            throw new EbookFormatException("The file isn't a MOBI book.");
        }

        var header = MobiHeader.Parse(database.ReadRecord(0));
        if (header.Encryption != 0)
        {
            throw new EbookFormatException("This book is protected with DRM, so it can't be opened.");
        }

        if (header.Version >= 8)
        {
            throw new EbookFormatException("This is a KF8 (AZW3) book, which isn't supported yet. Convert it to EPUB (for example with Calibre) to read it here.");
        }

        if (header.Compression is not (MobiHeader.NoCompression or MobiHeader.PalmDocCompression or MobiHeader.HuffCdicCompression))
        {
            throw new EbookFormatException($"The MOBI book uses an unknown compression ({header.Compression}).");
        }

        return header;
    }

    /// <summary>Reads an image record by its MOBI <c>recindex</c> (1-based, relative to the first image).</summary>
    public static byte[]? ReadImage(PalmDatabase database, MobiHeader header, int recindex)
    {
        var record = header.FirstImageIndex + recindex - 1;
        if (recindex < 1 || header.FirstImageIndex <= 0 || record >= database.RecordCount)
        {
            return null;
        }

        var data = database.ReadRecord(record);
        return ImageType(data) is null ? null : data;
    }

    public static string? ImageType(ReadOnlySpan<byte> data) => data switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
        [0x42, 0x4D, ..] => "image/bmp",
        _ => null,
    };

    public static string ImagePath(int recindex) => $"{ImageDirectory}/{recindex:D5}";

    private static byte[] DecompressText(PalmDatabase database, MobiHeader header)
    {
        var output = new List<byte>(Math.Max(header.TextLength, 4096));
        HuffCdicDecoder? huff = null;
        if (header.Compression == MobiHeader.HuffCdicCompression)
        {
            if (header.HuffRecordCount < 2)
            {
                throw new EbookFormatException("The MOBI book's Huffman records are missing.");
            }

            var cdics = Enumerable.Range(header.HuffRecordIndex + 1, header.HuffRecordCount - 1).Select(database.ReadRecord);
            huff = new HuffCdicDecoder(database.ReadRecord(header.HuffRecordIndex), cdics);
        }

        var last = Math.Min(header.TextRecordCount, database.RecordCount - 1);
        for (var i = 1; i <= last; i++)
        {
            var record = MobiDecompression.TrimTrailingEntries(database.ReadRecord(i), header.ExtraFlags);
            switch (header.Compression)
            {
                case MobiHeader.NoCompression:
                    foreach (var b in record)
                    {
                        output.Add(b);
                    }

                    break;
                case MobiHeader.PalmDocCompression:
                    MobiDecompression.PalmDoc(record, output);
                    break;
                default:
                    huff!.Decode(record, output);
                    break;
            }
        }

        var length = header.TextLength > 0 ? Math.Min(header.TextLength, output.Count) : output.Count;
        return output.GetRange(0, length).ToArray();
    }

    internal static MobiBook Convert(MobiHeader header, byte[] raw)
    {
        // Latin-1 maps every byte to one char, so string offsets stay equal to MOBI byte offsets
        // (which filepos links use) until the very end, when chunks are decoded properly.
        var text = Encoding.Latin1.GetString(raw);
        var utf8 = header.TextEncoding.CodePage == 65001;

        // Anchors go in first: filepos values are offsets into the original text.
        var tocPosition = FindTocPosition(text);
        text = InsertAnchors(text, utf8, tocPosition);
        text = Guide().Replace(text, string.Empty);

        var chunks = SplitChapters(text);
        var anchorChapter = new Dictionary<int, int>();
        for (var i = 0; i < chunks.Count; i++)
        {
            foreach (Match match in AnchorId().Matches(chunks[i]))
            {
                anchorChapter.TryAdd(int.Parse(match.Groups[1].Value), i);
            }
        }

        var chapters = new List<MobiChapter>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var html = RewriteLinks(chunks[i], anchorChapter);
            html = RewriteImages(html);
            var decoded = header.TextEncoding.GetString(Encoding.Latin1.GetBytes(html));
            chapters.Add(new MobiChapter(ChapterHref(i), decoded));
        }

        int? tocChapter = tocPosition is { } position && anchorChapter.TryGetValue(position, out var found) ? found : null;
        return new MobiBook(header, chapters, tocChapter);
    }

    public static string ChapterHref(int index) => $"{ChapterDirectory}/part{index + 1:D4}.html";

    private static int? FindTocPosition(string text)
    {
        foreach (Match reference in GuideReference().Matches(text))
        {
            var tag = reference.Value;
            if (TypeToc().IsMatch(tag) && FilePos().Match(tag) is { Success: true } position)
            {
                return int.Parse(position.Groups[1].Value);
            }
        }

        return null;
    }

    /// <summary>Puts an <c>id="fileposN"</c> anchor at every position a link points to.</summary>
    private static string InsertAnchors(string text, bool utf8, int? tocPosition)
    {
        var targets = new SortedSet<int>();
        foreach (Match match in FilePos().Matches(text))
        {
            targets.Add(int.Parse(match.Groups[1].Value));
        }

        if (tocPosition is { } toc)
        {
            targets.Add(toc);
        }

        var builder = new StringBuilder(text.Length + (targets.Count * 32));
        var copied = 0;
        foreach (var target in targets)
        {
            if (target < 0 || target > text.Length)
            {
                continue;
            }

            var position = target;

            // Never split a tag or a UTF-8 character.
            var lastOpen = text.LastIndexOf('<', Math.Max(0, position - 1));
            var lastClose = text.LastIndexOf('>', Math.Max(0, position - 1));
            if (position > 0 && lastOpen > lastClose)
            {
                var close = text.IndexOf('>', position);
                position = close < 0 ? text.Length : close + 1;
            }

            while (utf8 && position < text.Length && (text[position] & 0xC0) == 0x80)
            {
                position++;
            }

            position = Math.Max(position, copied);
            builder.Append(text, copied, position - copied);
            builder.Append("<a id=\"filepos").Append(target).Append("\"></a>");
            copied = position;
        }

        builder.Append(text, copied, text.Length - copied);
        return builder.ToString();
    }

    private static List<string> SplitChapters(string text)
    {
        var pieces = PageBreak().Split(text);
        var chunks = new List<string>();
        var carry = string.Empty;
        foreach (var piece in pieces)
        {
            var chunk = carry + piece;

            // Pieces with nothing to show (just anchors or wrappers) join the next chapter.
            if (!HasContent(chunk))
            {
                carry = chunk;
                continue;
            }

            carry = string.Empty;
            chunks.AddRange(SplitLarge(chunk));
        }

        if (carry.Length > 0)
        {
            if (chunks.Count > 0)
            {
                chunks[^1] += carry;
            }
            else
            {
                chunks.Add(carry);
            }
        }

        if (chunks.Count == 0)
        {
            throw new EbookFormatException("The MOBI book has no readable text.");
        }

        return chunks;
    }

    /// <summary>Very long sections are split at a paragraph or heading so pages lay out quickly.</summary>
    private static IEnumerable<string> SplitLarge(string chunk)
    {
        var start = 0;
        while (chunk.Length - start > MaxChapterChars)
        {
            var boundary = BlockStart().Match(chunk, start + (MaxChapterChars * 3 / 4));
            if (!boundary.Success || boundary.Index - start > MaxChapterChars * 2)
            {
                break;
            }

            yield return chunk[start..boundary.Index];
            start = boundary.Index;
        }

        yield return chunk[start..];
    }

    private static bool HasContent(string chunk) =>
        chunk.Contains("<img", StringComparison.OrdinalIgnoreCase)
        || Tag().Replace(chunk, string.Empty).Any(c => !char.IsWhiteSpace(c) && c != ' ');

    private static string RewriteLinks(string html, Dictionary<int, int> anchorChapter) =>
        LinkWithFilePos().Replace(html, match =>
        {
            var before = match.Groups["before"].Value;
            var after = match.Groups["after"].Value;
            var target = int.Parse(match.Groups["pos"].Value);
            return anchorChapter.TryGetValue(target, out var chapter)
                ? $"<a{before} href=\"part{chapter + 1:D4}.html#filepos{target}\"{after}>"
                : $"<a{before}{after}>";
        });

    private static string RewriteImages(string html) =>
        ImageTag().Replace(html, match =>
        {
            var tag = match.Value;
            var index = RecIndex().Match(tag);
            if (!index.Success)
            {
                return tag;
            }

            var cleaned = RecIndexAttribute().Replace(tag, string.Empty);
            var recindex = int.Parse(index.Groups[1].Value);
            return cleaned.Insert(4, $" src=\"../{ImagePath(recindex)}\"");
        });

    [GeneratedRegex(@"<guide\b.*?</guide>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Guide();

    [GeneratedRegex(@"<reference\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex GuideReference();

    [GeneratedRegex(@"\btype\s*=\s*[""']?toc\b", RegexOptions.IgnoreCase)]
    private static partial Regex TypeToc();

    [GeneratedRegex(@"\bfilepos\s*=\s*[""']?0*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex FilePos();

    [GeneratedRegex(@"<a\b(?<before>[^>]*?)\s*\bfilepos\s*=\s*[""']?0*(?<pos>\d+)[""']?(?<after>[^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkWithFilePos();

    [GeneratedRegex(@"id=""filepos(\d+)""")]
    private static partial Regex AnchorId();

    [GeneratedRegex(@"<mbp:pagebreak\b[^>]*>|</mbp:pagebreak>", RegexOptions.IgnoreCase)]
    private static partial Regex PageBreak();

    [GeneratedRegex(@"<(?:p|h[1-6]|div|blockquote)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BlockStart();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImageTag();

    [GeneratedRegex(@"\brecindex\s*=\s*[""']?0*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RecIndex();

    [GeneratedRegex(@"\s*\b(?:hi|lo)?recindex\s*=\s*[""']?\d+[""']?", RegexOptions.IgnoreCase)]
    private static partial Regex RecIndexAttribute();
}

internal sealed record MobiChapter(string Href, string Html);
