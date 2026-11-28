using System.Text.RegularExpressions;
using CoffeeReader.Application.Abstractions;
using CoffeeReader.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace CoffeeReader.Infrastructure.Ebooks.Pdf;

/// <summary>
/// PDF pages are drawn in the browser (PDF.js); the server only supplies page text for search.
/// </summary>
public sealed partial class PdfContentProvider : IEbookContentProvider, IDisposable
{
    private readonly MemoryCache _texts = new(new MemoryCacheOptions { SizeLimit = 64 * 1024 * 1024 });

    public BookFormat Format => BookFormat.Pdf;

    public Task<ChapterDocument> GetChapterAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken) =>
        throw new NotSupportedException("PDF pages are rendered in the browser.");

    public async Task<ChapterText> GetChapterTextAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken)
    {
        var pages = await PageTextsAsync(fullPath, fileHash, cancellationToken);
        var index = int.TryParse(chapterHref.AsSpan(PdfParser.PagePrefix.Length), out var number) ? number - 1 : -1;
        return new ChapterText(index >= 0 && index < pages.Length ? pages[index] : string.Empty, []);
    }

    public Task<BookResource?> OpenResourceAsync(string fullPath, string resourcePath, CancellationToken cancellationToken) =>
        Task.FromResult<BookResource?>(null);

    public void Dispose() => _texts.Dispose();

    private async Task<string[]> PageTextsAsync(string fullPath, string fileHash, CancellationToken cancellationToken)
    {
        if (_texts.TryGetValue(fileHash, out string[]? cached) && cached is not null)
        {
            return cached;
        }

        var pages = await Task.Run(() => Pdfium.Use(() =>
        {
            using var document = PdfDocument.Open(fullPath);
            return Enumerable.Range(0, document.PageCount).Select(i => Normalize(document.PageText(i))).ToArray();
        }), cancellationToken);

        _texts.Set(fileHash, pages, new MemoryCacheEntryOptions
        {
            Size = Math.Max(1, pages.Sum(p => (long)p.Length * sizeof(char))),
            SlidingExpiration = TimeSpan.FromMinutes(30),
        });
        return pages;
    }

    /// <summary>Joins words hyphenated across line breaks and turns line breaks into spaces.</summary>
    internal static string Normalize(string text) =>
        LineBreak().Replace(Hyphenation().Replace(text, "$1"), " ").Replace('\u0002', '-');

    [GeneratedRegex(@"(\p{Ll})[-­\u0002]\r?\n(?=\p{Ll})")]
    private static partial Regex Hyphenation();

    [GeneratedRegex(@"\s*\r?\n\s*")]
    private static partial Regex LineBreak();
}
