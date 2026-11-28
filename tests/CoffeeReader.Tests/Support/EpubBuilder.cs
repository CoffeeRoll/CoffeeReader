using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace CoffeeReader.Tests.Support;

/// <summary>
/// Builds small EPUB files for tests.
/// </summary>
public sealed class EpubBuilder
{
    private readonly List<(string Id, string Title, string Body)> _chapters = [];
    private readonly List<string> _authors = [];
    private string _title = "Test Book";
    private string? _series;
    private string? _seriesIndex;
    private bool _epub2;
    private bool _withCover = true;
    private string? _description;
    private string _identifier = "urn:uuid:" + Guid.NewGuid();
    private string _extraCss = "p { text-indent: 1em; }";

    public EpubBuilder Title(string title)
    {
        _title = title;
        return this;
    }

    public EpubBuilder Author(string author)
    {
        _authors.Add(author);
        return this;
    }

    public EpubBuilder Series(string name, string index)
    {
        _series = name;
        _seriesIndex = index;
        return this;
    }

    public EpubBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public EpubBuilder Epub2()
    {
        _epub2 = true;
        return this;
    }

    public EpubBuilder WithoutCover()
    {
        _withCover = false;
        return this;
    }

    public EpubBuilder Identifier(string identifier)
    {
        _identifier = identifier;
        return this;
    }

    public EpubBuilder Chapter(string title, string bodyHtml)
    {
        _chapters.Add(($"ch{_chapters.Count + 1}", title, bodyHtml));
        return this;
    }

    public byte[] Build()
    {
        if (_chapters.Count == 0)
        {
            Chapter("Chapter One", "<p>It was a bright cold day in April.</p>");
        }

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(mimetype.Open(), new UTF8Encoding(false)))
            {
                writer.Write("application/epub+zip");
            }

            Add(zip, "META-INF/container.xml", """
                <?xml version="1.0"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
                </container>
                """);
            Add(zip, "OEBPS/Styles/book.css", _extraCss);

            foreach (var (id, title, body) in _chapters)
            {
                Add(zip, $"OEBPS/Text/{id}.xhtml", $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <!DOCTYPE html>
                    <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="en">
                    <head><title>{title}</title><link rel="stylesheet" type="text/css" href="../Styles/book.css"/></head>
                    <body class="calibre"><h1 id="{id}-title">{title}</h1>{body}</body>
                    </html>
                    """);
            }

            if (_withCover)
            {
                zip.CreateEntry("OEBPS/Images/cover.png").Open().Using(s => s.Write(CreatePng(300, 450)));
            }

            Add(zip, "OEBPS/content.opf", Opf());
            if (_epub2)
            {
                Add(zip, "OEBPS/toc.ncx", Ncx());
            }
            else
            {
                Add(zip, "OEBPS/nav.xhtml", Nav());
            }
        }

        return buffer.ToArray();
    }

    public string WriteTo(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, Build());
        return path;
    }

    public static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(120, 70, 40));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private string Opf()
    {
        var authors = string.Join("\n", _authors.Select((a, i) => _epub2
            ? $"<dc:creator opf:role=\"aut\" opf:file-as=\"{FileAs(a)}\">{a}</dc:creator>"
            : $"<dc:creator id=\"creator{i}\">{a}</dc:creator><meta refines=\"#creator{i}\" property=\"role\" scheme=\"marc:relators\">aut</meta>"));
        var series = _series is null ? string.Empty : _epub2
            ? $"<meta name=\"calibre:series\" content=\"{_series}\"/><meta name=\"calibre:series_index\" content=\"{_seriesIndex}\"/>"
            : $"<meta property=\"belongs-to-collection\" id=\"c1\">{_series}</meta><meta refines=\"#c1\" property=\"collection-type\">series</meta><meta refines=\"#c1\" property=\"group-position\">{_seriesIndex}</meta>";
        var cover = !_withCover ? string.Empty : _epub2 ? "<meta name=\"cover\" content=\"cover-image\"/>" : string.Empty;
        var description = _description is null ? string.Empty : $"<dc:description>{System.Security.SecurityElement.Escape(_description)}</dc:description>";
        var manifest = string.Join("\n", _chapters.Select(c => $"<item id=\"{c.Id}\" href=\"Text/{c.Id}.xhtml\" media-type=\"application/xhtml+xml\"/>"));
        var spine = string.Join("\n", _chapters.Select(c => $"<itemref idref=\"{c.Id}\"/>"));
        var coverItem = _withCover ? $"<item id=\"cover-image\" href=\"Images/cover.png\" media-type=\"image/png\"{(_epub2 ? string.Empty : " properties=\"cover-image\"")}/>" : string.Empty;
        var navItem = _epub2
            ? "<item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>"
            : "<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>";

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" xmlns:opf="http://www.idpf.org/2007/opf" version="{(_epub2 ? "2.0" : "3.0")}" unique-identifier="bookid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">{_identifier}</dc:identifier>
                <dc:title>{_title}</dc:title>
                <dc:language>en</dc:language>
                <dc:publisher>Test Press</dc:publisher>
                <dc:date>2001-05-17T00:00:00+00:00</dc:date>
                {authors}
                {series}
                {cover}
                {description}
              </metadata>
              <manifest>
                {navItem}
                <item id="css" href="Styles/book.css" media-type="text/css"/>
                {coverItem}
                {manifest}
              </manifest>
              <spine{(_epub2 ? " toc=\"ncx\"" : string.Empty)}>
                {spine}
              </spine>
            </package>
            """;
    }

    private string Nav()
    {
        var items = string.Join("\n", _chapters.Select(c => $"<li><a href=\"Text/{c.Id}.xhtml\">{c.Title}</a></li>"));
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Contents</title></head>
            <body><nav epub:type="toc"><h1>Contents</h1><ol>{items}</ol></nav></body>
            </html>
            """;
    }

    private string Ncx()
    {
        var points = string.Join("\n", _chapters.Select((c, i) =>
            $"<navPoint id=\"np{i}\" playOrder=\"{i + 1}\"><navLabel><text>{c.Title}</text></navLabel><content src=\"Text/{c.Id}.xhtml\"/></navPoint>"));
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head/>
              <docTitle><text>{_title}</text></docTitle>
              <navMap>{points}</navMap>
            </ncx>
            """;
    }

    private static string FileAs(string name)
    {
        var parts = name.Split(' ');
        return parts.Length < 2 ? name : $"{parts[^1]}, {string.Join(' ', parts[..^1])}";
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.TrimStart());
    }
}

internal static class StreamExtensions
{
    public static void Using(this Stream stream, Action<Stream> action)
    {
        using (stream)
        {
            action(stream);
        }
    }
}

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "coffeereader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
