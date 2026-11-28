using CoffeeReader.Application.Abstractions;
using CoffeeReader.Application.Common;
using CoffeeReader.Domain.Common;
using CoffeeReader.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CoffeeReader.Application.Books;

public sealed class BookService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ILibraryFileSystem files,
    ICoverStore covers,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<BookSummaryDto>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        return await db.Books.AsNoTracking()
            .Where(b => !b.IsMissing)
            .OrderBy(b => b.SortTitle)
            .Select(b => new BookSummaryDto(
                b.Id,
                b.Title,
                b.SortTitle,
                b.Authors.OrderBy(a => a.Order).Select(a => new AuthorRefDto(a.Author.Id, a.Author.Name, a.Author.SortName)).ToList(),
                b.Series == null ? null : new SeriesRefDto(b.Series.Id, b.Series.Name),
                b.SeriesIndex,
                b.HasCover,
                b.UpdatedAt.Ticks,
                b.AddedAt,
                db.ReadingProgress
                    .Where(p => p.UserId == userId && p.BookId == b.Id)
                    .Select(p => new ProgressSummaryDto(p.Percentage, p.IsFinished, p.UpdatedAt))
                    .FirstOrDefault(),
                b.Format))
            .ToListAsync(cancellationToken);
    }

    public async Task<BookDetailsDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var details = await db.Books.AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new BookDetailsDto(
                b.Id,
                b.Title,
                b.SortTitle,
                b.Authors.OrderBy(a => a.Order).Select(a => new AuthorRefDto(a.Author.Id, a.Author.Name, a.Author.SortName)).ToList(),
                b.Series == null ? null : new SeriesRefDto(b.Series.Id, b.Series.Name),
                b.SeriesIndex,
                b.HasCover,
                b.UpdatedAt.Ticks,
                b.AddedAt,
                db.ReadingProgress
                    .Where(p => p.UserId == userId && p.BookId == b.Id)
                    .Select(p => new ProgressSummaryDto(p.Percentage, p.IsFinished, p.UpdatedAt))
                    .FirstOrDefault(),
                b.Description,
                b.Publisher,
                b.Language,
                b.PublishedDate,
                b.Isbn,
                b.FilePath,
                b.FileSize,
                b.Format,
                b.Chapters.Count,
                b.MetadataLocked,
                db.ShelfBooks
                    .Where(sb => sb.BookId == b.Id && db.Shelves.Any(s => s.Id == sb.ShelfId && s.UserId == userId))
                    .Select(sb => sb.ShelfId)
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFoundException.For("Book", id);

        return details with { FileName = Path.GetFileName(details.FileName) };
    }

    public async Task<BookDetailsDto> UpdateAsync(int id, UpdateBookRequest request, CancellationToken cancellationToken)
    {
        currentUser.RequireAdmin();
        var book = await db.Books
            .Include(b => b.Authors).ThenInclude(a => a.Author)
            .Include(b => b.Series)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Book", id);

        if (request.SeriesIndex is < 0 or > 100_000)
        {
            throw ValidationException.For("seriesIndex", "Series number must be between 0 and 100000.");
        }

        var resolver = new CatalogResolver(db);
        var authors = await resolver.ResolveAuthorsAsync(
            (request.Authors ?? []).Select(a => (a, (string?)null)), cancellationToken);
        var series = await resolver.ResolveSeriesAsync(request.Series, cancellationToken);
        var metadata = new BookMetadata(
            request.Title ?? string.Empty,
            request.Description,
            request.Publisher,
            request.Language,
            request.PublishedDate,
            request.Isbn,
            book.Identifier);

        try
        {
            book.EditMetadata(metadata, authors, series, request.SeriesIndex, clock.GetUtcNow().UtcDateTime);
        }
        catch (DomainException ex)
        {
            throw ValidationException.For("title", ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        await resolver.RemoveOrphansAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Removes a book from the library and deletes its file, so a rescan doesn't bring it back.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        currentUser.RequireAdmin();
        var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw NotFoundException.For("Book", id);

        try
        {
            files.Delete(book.FilePath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new ConflictException("The book's file couldn't be deleted. The library folder may be read-only.");
        }

        db.Books.Remove(book);
        await db.SaveChangesAsync(cancellationToken);
        covers.Delete(id);
        await new CatalogResolver(db).RemoveOrphansAsync(cancellationToken);
    }

    public CoverImage? OpenCover(int id)
    {
        currentUser.RequireUserId();
        return covers.Open(id);
    }

    public async Task<BookDownload> GetDownloadAsync(int id, CancellationToken cancellationToken)
    {
        currentUser.RequireUserId();
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id && !b.IsMissing, cancellationToken)
            ?? throw NotFoundException.For("Book", id);

        var extension = Path.GetExtension(book.FilePath);
        var fileName = SafeFileName(book.Title) + extension;
        var fullPath = files.GetFullPath(book.FilePath);
        return new BookDownload(fullPath, fileName, ContentTypes.ForFormat(book.Format), File.GetLastWriteTimeUtc(fullPath), book.FileHash);
    }

    private static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(c => invalid.Contains(c) || c is '"' or '\\' ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "book" : cleaned;
    }
}
