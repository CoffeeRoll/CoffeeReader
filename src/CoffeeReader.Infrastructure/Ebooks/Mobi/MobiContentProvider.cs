using CoffeeReader.Application.Abstractions;
using CoffeeReader.Domain.Entities;
using CoffeeReader.Infrastructure.Ebooks.Html;
using Microsoft.Extensions.Caching.Memory;

namespace CoffeeReader.Infrastructure.Ebooks.Mobi;

/// <summary>
/// Serves MOBI books through the same HTML reader as EPUB: chapters converted from the MOBI text,
/// images straight from the book's image records.
/// </summary>
public sealed class MobiContentProvider : IEbookContentProvider, IDisposable
{
    private readonly ChapterCache _chapters = new();
    private readonly MemoryCache _books = new(new MemoryCacheOptions { SizeLimit = 96 * 1024 * 1024 });

    public BookFormat Format => BookFormat.Mobi;

    public Task<ChapterDocument> GetChapterAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken) =>
        _chapters.GetChapterAsync($"{fileHash}:{chapterHref}", () => RenderAsync(fullPath, fileHash, chapterHref, cancellationToken));

    public Task<ChapterText> GetChapterTextAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken) =>
        _chapters.GetTextAsync($"{fileHash}:{chapterHref}", () => RenderAsync(fullPath, fileHash, chapterHref, cancellationToken));

    public Task<BookResource?> OpenResourceAsync(string fullPath, string resourcePath, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var path = ContainerPath.Resolve(string.Empty, resourcePath);
            var prefix = MobiBook.ImageDirectory + "/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(path[prefix.Length..], out var recindex))
            {
                return null;
            }

            using var database = PalmDatabase.Open(fullPath);
            var header = MobiBook.ReadHeader(database);
            var image = MobiBook.ReadImage(database, header, recindex);
            return image is null ? null : new BookResource(image, MobiBook.ImageType(image)!);
        }, cancellationToken);

    public void Dispose()
    {
        _chapters.Dispose();
        _books.Dispose();
    }

    private async Task<ChapterDocument> RenderAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken)
    {
        var book = await LoadAsync(fullPath, fileHash, cancellationToken);
        var chapter = book.Chapters.FirstOrDefault(c => string.Equals(c.Href, chapterHref, StringComparison.OrdinalIgnoreCase))
            ?? throw new EbookFormatException($"The section '{chapterHref}' isn't part of this book.");
        return HtmlChapterRenderer.Render(chapter.Html, chapter.Href);
    }

    private async Task<MobiBook> LoadAsync(string fullPath, string fileHash, CancellationToken cancellationToken)
    {
        if (_books.TryGetValue(fileHash, out MobiBook? cached) && cached is not null)
        {
            return cached;
        }

        var book = await Task.Run(() => MobiBook.Load(fullPath), cancellationToken);
        _books.Set(fileHash, book, new MemoryCacheEntryOptions { Size = Math.Max(1, book.Size), SlidingExpiration = TimeSpan.FromMinutes(20) });
        return book;
    }
}
