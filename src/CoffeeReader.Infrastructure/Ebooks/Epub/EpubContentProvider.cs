using CoffeeReader.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace CoffeeReader.Infrastructure.Ebooks.Epub;

/// <summary>
/// Serves chapters and embedded files out of EPUB archives, caching processed chapters in memory.
/// </summary>
public sealed class EpubContentProvider : IEbookContentProvider, IDisposable
{
    private static readonly Dictionary<string, string> ResourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".css"] = "text/css; charset=utf-8",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".avif"] = "image/avif",
        [".svg"] = "image/svg+xml",
        [".bmp"] = "image/bmp",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".mp4"] = "video/mp4",
    };

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 48 * 1024 * 1024 });

    public Task<ChapterDocument> GetChapterAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync($"chapter:{fileHash}:{chapterHref}", entry =>
        {
            var document = Task.Run(() => RenderChapter(fullPath, chapterHref), cancellationToken);
            return document.ContinueWith(t =>
            {
                var result = t.GetAwaiter().GetResult();
                entry.Size = (result.BodyHtml.Length + result.InlineStyles.Sum(s => s.Length)) * sizeof(char);
                entry.SlidingExpiration = TimeSpan.FromMinutes(30);
                return result;
            }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        })!;

    public async Task<string> GetChapterTextAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken)
    {
        var key = $"text:{fileHash}:{chapterHref}";
        if (_cache.TryGetValue(key, out string? cached) && cached is not null)
        {
            return cached;
        }

        var chapter = await GetChapterAsync(fullPath, fileHash, chapterHref, cancellationToken);
        var text = ChapterText(chapter.BodyHtml);
        _cache.Set(key, text, new MemoryCacheEntryOptions { Size = text.Length * sizeof(char), SlidingExpiration = TimeSpan.FromMinutes(30) });
        return text;
    }

    public Task<BookResource?> OpenResourceAsync(string fullPath, string resourcePath, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var path = EpubPackage.ResolvePath(string.Empty, resourcePath);
            var extension = Path.GetExtension(path);

            // Content documents are only served through the sanitizing chapter endpoint.
            if (!ResourceTypes.TryGetValue(extension, out var contentType))
            {
                return null;
            }

            using var package = EpubPackage.Open(fullPath);
            var bytes = package.ReadBytes(path);
            return bytes is null ? null : new BookResource(bytes, contentType);
        }, cancellationToken);

    public void Dispose() => _cache.Dispose();

    /// <summary>The text a browser would produce for the chapter body (its <c>textContent</c>).</summary>
    internal static string ChapterText(string bodyHtml)
    {
        var document = XhtmlLoader.CreateEmptyDocument();
        document.Body!.InnerHtml = bodyHtml;
        return document.Body.TextContent;
    }

    internal static ChapterDocument RenderChapter(string fullPath, string chapterHref)
    {
        using var package = EpubPackage.Open(fullPath);
        var source = package.ReadText(chapterHref)
            ?? throw new EbookFormatException($"The chapter '{chapterHref}' is missing from the book.");
        return Render(source, chapterHref);
    }

    internal static ChapterDocument Render(string source, string chapterHref)
    {
        var document = XhtmlLoader.Parse(source);
        var directory = EpubPackage.DirectoryOf(chapterHref);

        var stylesheets = document.QuerySelectorAll("link[href]")
            .Where(l => (l.GetAttribute("rel") ?? string.Empty).Split(' ').Contains("stylesheet", StringComparer.OrdinalIgnoreCase))
            .Select(l => EpubPackage.ResolvePath(directory, l.GetAttribute("href")!))
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
