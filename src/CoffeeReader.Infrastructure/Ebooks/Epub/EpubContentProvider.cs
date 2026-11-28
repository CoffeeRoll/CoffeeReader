using CoffeeReader.Application.Abstractions;
using CoffeeReader.Domain.Entities;
using CoffeeReader.Infrastructure.Ebooks.Html;

namespace CoffeeReader.Infrastructure.Ebooks.Epub;

/// <summary>
/// Serves chapters and embedded files out of EPUB archives, caching processed chapters in memory.
/// </summary>
public sealed class EpubContentProvider : IEbookContentProvider, IDisposable
{
    public BookFormat Format => BookFormat.Epub;

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

    private readonly ChapterCache _cache = new();

    public Task<ChapterDocument> GetChapterAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken) =>
        _cache.GetChapterAsync($"{fileHash}:{chapterHref}", () => Task.Run(() => RenderChapter(fullPath, chapterHref), cancellationToken));

    public Task<ChapterText> GetChapterTextAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken) =>
        _cache.GetTextAsync($"{fileHash}:{chapterHref}", () => Task.Run(() => RenderChapter(fullPath, chapterHref), cancellationToken));

    public Task<BookResource?> OpenResourceAsync(string fullPath, string resourcePath, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var path = ContainerPath.Resolve(string.Empty, resourcePath);
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

    internal static ChapterDocument RenderChapter(string fullPath, string chapterHref)
    {
        using var package = EpubPackage.Open(fullPath);
        var source = package.ReadText(chapterHref)
            ?? throw new EbookFormatException($"The chapter '{chapterHref}' is missing from the book.");
        return HtmlChapterRenderer.Render(source, chapterHref);
    }

}
