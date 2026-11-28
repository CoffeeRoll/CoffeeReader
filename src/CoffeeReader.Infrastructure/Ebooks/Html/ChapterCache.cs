using CoffeeReader.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace CoffeeReader.Infrastructure.Ebooks.Html;

/// <summary>
/// Keeps recently read chapters (rendered HTML and their search text) in memory, bounded by size.
/// </summary>
public sealed class ChapterCache : IDisposable
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 64 * 1024 * 1024 });

    public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> create, Func<T, long> size)
        where T : class
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await create();
        _cache.Set(key, value, new MemoryCacheEntryOptions { Size = Math.Max(1, size(value)), SlidingExpiration = Idle });
        return value;
    }

    public Task<ChapterDocument> GetChapterAsync(string key, Func<Task<ChapterDocument>> render) =>
        GetOrAddAsync($"chapter:{key}", render, c => (c.BodyHtml.Length + c.InlineStyles.Sum(s => s.Length)) * sizeof(char));

    public async Task<ChapterText> GetTextAsync(string key, Func<Task<ChapterDocument>> render) =>
        await GetOrAddAsync(
            $"text:{key}",
            async () => HtmlChapterRenderer.ExtractText((await GetChapterAsync(key, render)).BodyHtml),
            t => (t.Text.Length * sizeof(char)) + (t.BlockBreaks.Count * sizeof(int)));

    public void Dispose() => _cache.Dispose();
}
