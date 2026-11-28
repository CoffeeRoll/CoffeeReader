using CoffeeReader.Application.Abstractions;
using CoffeeReader.Application.Common;
using CoffeeReader.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CoffeeReader.Application.Reading;

/// <summary>
/// Everything the reader needs to display a book: its structure, chapters, embedded files and search.
/// </summary>
public sealed class ReaderService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ILibraryFileSystem files,
    IEnumerable<IEbookContentProvider> providers)
{
    private readonly IReadOnlyList<IEbookContentProvider> _providers = providers.ToList();

    public const int MinQueryLength = 2;
    public const int MaxHits = 500;

    public async Task<ReaderManifestDto> GetManifestAsync(int bookId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var book = await LoadBookAsync(bookId, includeToc: true, cancellationToken);
        var progress = await db.ReadingProgress.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == bookId, cancellationToken);

        return new ReaderManifestDto(
            book.Id,
            book.Format,
            book.FileHash.Length > 12 ? book.FileHash[..12] : book.FileHash,
            book.Title,
            book.Authors.OrderBy(a => a.Order).Select(a => a.Author.Name).ToList(),
            book.Language,
            book.Chapters.OrderBy(c => c.Index).Select(c => new ChapterInfoDto(c.Index, c.Href, c.Title, c.Length, c.IsLinear)).ToList(),
            book.TableOfContents.OrderBy(t => t.Order).Select(t => new TocEntryDto(t.Title, t.Depth, t.ChapterIndex, t.Fragment)).ToList(),
            book.TotalLength,
            progress is null ? null : ProgressService.ToDto(progress, book.Chapters.Count));
    }

    public async Task<ChapterDto> GetChapterAsync(int bookId, int index, CancellationToken cancellationToken)
    {
        currentUser.RequireUserId();
        var book = await LoadBookAsync(bookId, includeToc: false, cancellationToken);
        var chapter = book.Chapters.FirstOrDefault(c => c.Index == index)
            ?? throw NotFoundException.For("Chapter", index);

        if (book.Format == BookFormat.Pdf)
        {
            throw new ValidationException("PDF pages are drawn by the reader in your browser; there are no HTML chapters.");
        }

        var document = await ProviderFor(book.Format).GetChapterAsync(files.GetFullPath(book.FilePath), book.FileHash, chapter.Href, cancellationToken);
        return new ChapterDto(
            chapter.Index,
            chapter.Href,
            document.BodyHtml,
            document.Stylesheets,
            document.InlineStyles,
            document.BodyId,
            document.BodyClass,
            document.BodyStyle,
            document.Language ?? book.Language,
            document.Direction);
    }

    public async Task<BookResource> GetResourceAsync(int bookId, string path, CancellationToken cancellationToken)
    {
        currentUser.RequireUserId();
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId && !b.IsMissing, cancellationToken)
            ?? throw NotFoundException.For("Book", bookId);

        return await ProviderFor(book.Format).OpenResourceAsync(files.GetFullPath(book.FilePath), path, cancellationToken)
            ?? throw new NotFoundException($"'{path}' isn't part of this book.");
    }

    public async Task<SearchResponseDto> SearchAsync(int bookId, string? query, CancellationToken cancellationToken)
    {
        currentUser.RequireUserId();
        query = (query ?? string.Empty).Trim();
        if (TextSearcher.FoldQuery(query).Length < MinQueryLength)
        {
            throw ValidationException.For("q", $"Search for at least {MinQueryLength} characters.");
        }

        var book = await LoadBookAsync(bookId, includeToc: false, cancellationToken);
        var fullPath = files.GetFullPath(book.FilePath);
        var content = ProviderFor(book.Format);
        var hits = new List<SearchHitDto>();
        var total = 0;

        foreach (var chapter in book.Chapters.OrderBy(c => c.Index))
        {
            var chapterText = await content.GetChapterTextAsync(fullPath, book.FileHash, chapter.Href, cancellationToken);
            var text = chapterText.Text;
            var occurrence = 0;
            foreach (var match in TextSearcher.FindAll(text, query))
            {
                total++;
                if (hits.Count < MaxHits)
                {
                    var snippet = TextSearcher.CreateSnippet(text, match, chapterText.BlockBreaks);
                    var withinChapter = text.Length == 0 ? 0 : (double)match.Start / text.Length;
                    hits.Add(new SearchHitDto(
                        chapter.Index,
                        chapter.Title,
                        occurrence,
                        snippet.Before,
                        snippet.Match,
                        snippet.After,
                        book.CalculatePercentage(chapter.Index, withinChapter)));
                }

                occurrence++;
            }
        }

        return new SearchResponseDto(query, total, total > hits.Count, hits);
    }

    private IEbookContentProvider ProviderFor(BookFormat format) =>
        _providers.FirstOrDefault(p => p.Format == format)
        ?? throw new ValidationException($"{format} books can't be opened by this server.");

    private async Task<Book> LoadBookAsync(int bookId, bool includeToc, CancellationToken cancellationToken)
    {
        IQueryable<Book> query = db.Books.AsNoTracking()
            .Include(b => b.Chapters)
            .Include(b => b.Authors).ThenInclude(a => a.Author);
        if (includeToc)
        {
            query = query.Include(b => b.TableOfContents);
        }

        return await query.FirstOrDefaultAsync(b => b.Id == bookId && !b.IsMissing, cancellationToken)
            ?? throw NotFoundException.For("Book", bookId);
    }
}
