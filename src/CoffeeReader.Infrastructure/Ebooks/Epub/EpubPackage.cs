using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CoffeeReader.Infrastructure.Ebooks.Html;
using CoffeeReader.Application.Abstractions;

namespace CoffeeReader.Infrastructure.Ebooks.Epub;

/// <summary>
/// An opened EPUB container: the zip, its package document (OPF), manifest and spine.
/// </summary>
internal sealed class EpubPackage : IDisposable
{
    public static readonly XNamespace OpfNs = "http://www.idpf.org/2007/opf";
    public static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    public static readonly XNamespace NcxNs = "http://www.daisy.org/z3986/2005/ncx/";
    public static readonly XNamespace ContainerNs = "urn:oasis:names:tc:opendocument:xmlns:container";

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        IgnoreComments = true,
    };

    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly Dictionary<string, ZipArchiveEntry> _entriesIgnoreCase;

    private EpubPackage(ZipArchive archive)
    {
        _archive = archive;
        _entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        _entriesIgnoreCase = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/').TrimStart('/');
            _entries.TryAdd(name, entry);
            _entriesIgnoreCase.TryAdd(name, entry);
        }
    }

    public string OpfPath { get; private set; } = string.Empty;

    public string OpfDirectory { get; private set; } = string.Empty;

    public XDocument Opf { get; private set; } = null!;

    public string? Version { get; private set; }

    public IReadOnlyDictionary<string, ManifestItem> Manifest { get; private set; } = new Dictionary<string, ManifestItem>();

    public IReadOnlyList<SpineItem> Spine { get; private set; } = [];

    public string? NcxId { get; private set; }

    public static EpubPackage Open(string fullPath)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(fullPath);
        }
        catch (InvalidDataException ex)
        {
            throw new EbookFormatException("The file isn't a valid EPUB (it isn't a zip archive).", ex);
        }

        var package = new EpubPackage(archive);
        try
        {
            package.Load();
            return package;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    public ZipArchiveEntry? FindEntry(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return _entries.GetValueOrDefault(normalized) ?? _entriesIgnoreCase.GetValueOrDefault(normalized);
    }

    public byte[]? ReadBytes(string path)
    {
        var entry = FindEntry(path);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var buffer = new MemoryStream(entry.Length > 0 && entry.Length < int.MaxValue ? (int)entry.Length : 0);
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public string? ReadText(string path)
    {
        var entry = FindEntry(path);
        if (entry is null)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public XDocument? ReadXml(string path)
    {
        var entry = FindEntry(path);
        if (entry is null)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, XmlSettings);
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    public ManifestItem? FindManifestItemByPath(string path) =>
        Manifest.Values.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.Ordinal))
        ?? Manifest.Values.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));

    public void Dispose() => _archive.Dispose();

    private void Load()
    {
        if (FindEntry("META-INF/container.xml") is null)
        {
            throw new EbookFormatException("The file isn't a valid EPUB (META-INF/container.xml is missing).");
        }

        var container = ReadXml("META-INF/container.xml")
            ?? throw new EbookFormatException("The EPUB's container.xml couldn't be read.");
        var rootFile = container.Descendants(ContainerNs + "rootfile")
            .Concat(container.Descendants().Where(e => e.Name.LocalName == "rootfile"))
            .Select(e => (string?)e.Attribute("full-path"))
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && p.EndsWith(".opf", StringComparison.OrdinalIgnoreCase))
            ?? throw new EbookFormatException("The EPUB doesn't say where its package document is.");

        OpfPath = ContainerPath.Resolve(string.Empty, rootFile);
        OpfDirectory = ContainerPath.DirectoryOf(OpfPath);
        Opf = ReadXml(OpfPath) ?? throw new EbookFormatException($"The EPUB package document ({OpfPath}) is missing or malformed.");
        Version = (string?)Opf.Root?.Attribute("version");

        var manifest = new Dictionary<string, ManifestItem>(StringComparer.Ordinal);
        foreach (var item in Opf.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            var id = (string?)item.Attribute("id");
            var href = (string?)item.Attribute("href");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            manifest.TryAdd(id, new ManifestItem(
                id,
                ContainerPath.Resolve(OpfDirectory, href),
                ((string?)item.Attribute("media-type") ?? string.Empty).Trim().ToLowerInvariant(),
                ((string?)item.Attribute("properties") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }

        Manifest = manifest;

        var spineElement = Opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "spine");
        NcxId = (string?)spineElement?.Attribute("toc");
        var spine = new List<SpineItem>();
        foreach (var itemRef in spineElement?.Elements().Where(e => e.Name.LocalName == "itemref") ?? [])
        {
            var idRef = (string?)itemRef.Attribute("idref");
            if (idRef is null || !manifest.TryGetValue(idRef, out var item) || !item.IsDocument)
            {
                continue;
            }

            if (FindEntry(item.Path) is null)
            {
                continue;
            }

            var linear = !string.Equals((string?)itemRef.Attribute("linear"), "no", StringComparison.OrdinalIgnoreCase);
            spine.Add(new SpineItem(item, linear));
        }

        if (spine.Count == 0)
        {
            throw new EbookFormatException("The EPUB has no readable content.");
        }

        Spine = spine;
    }
}

internal sealed record ManifestItem(string Id, string Path, string MediaType, IReadOnlyList<string> Properties)
{
    public bool IsDocument => MediaType is "application/xhtml+xml" or "text/html" or "application/xml" or "text/xml"
        || Path.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase)
        || Path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        || Path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

    public bool IsImage => MediaType.StartsWith("image/", StringComparison.Ordinal);

    public bool HasProperty(string property) => Properties.Contains(property, StringComparer.OrdinalIgnoreCase);
}

internal sealed record SpineItem(ManifestItem Item, bool IsLinear);
