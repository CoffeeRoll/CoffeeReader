using CoffeeReader.Application.Abstractions;
using CoffeeReader.Application.Common;
using CoffeeReader.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CoffeeReader.Application.Reading;

public sealed class ProgressService(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<ProgressDto?> GetAsync(int bookId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var chapterCount = await db.Books.Where(b => b.Id == bookId).Select(b => (int?)b.Chapters.Count).FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFoundException.For("Book", bookId);
        var progress = await db.ReadingProgress.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == bookId, cancellationToken);
        return progress is null ? null : ToDto(progress, chapterCount);
    }

    public async Task<ProgressDto> SaveAsync(int bookId, SaveProgressRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        if (!double.IsFinite(request.ChapterProgress))
        {
            throw ValidationException.For("chapterProgress", "Progress must be a number between 0 and 1.");
        }

        var book = await db.Books.AsNoTracking().Include(b => b.Chapters)
            .FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken)
            ?? throw NotFoundException.For("Book", bookId);

        if (request.ChapterIndex < 0 || request.ChapterIndex >= book.Chapters.Count)
        {
            throw ValidationException.For("chapterIndex", "That chapter doesn't exist.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var progress = await GetOrCreateAsync(userId, bookId, now, cancellationToken);
        progress.Update(request.ChapterIndex, request.ChapterProgress, book.CalculatePercentage(request.ChapterIndex, request.ChapterProgress), now);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(progress, book.Chapters.Count);
    }

    public async Task<ProgressDto?> SetFinishedAsync(int bookId, bool finished, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var chapterCount = await db.Books.Where(b => b.Id == bookId).Select(b => (int?)b.Chapters.Count).FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFoundException.For("Book", bookId);
        var now = clock.GetUtcNow().UtcDateTime;

        if (!finished)
        {
            // "Mark as unread" starts the book over.
            await db.ReadingProgress.Where(p => p.UserId == userId && p.BookId == bookId).ExecuteDeleteAsync(cancellationToken);
            return null;
        }

        var progress = await GetOrCreateAsync(userId, bookId, now, cancellationToken);
        progress.MarkFinished(now);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(progress, chapterCount);
    }

    internal static ProgressDto ToDto(ReadingProgress progress, int chapterCount)
    {
        var chapterIndex = chapterCount == 0 ? 0 : Math.Clamp(progress.ChapterIndex, 0, chapterCount - 1);
        return new ProgressDto(chapterIndex, progress.ChapterProgress, progress.Percentage, progress.IsFinished, progress.UpdatedAt, progress.FinishedAt);
    }

    private async Task<ReadingProgress> GetOrCreateAsync(int userId, int bookId, DateTime now, CancellationToken cancellationToken)
    {
        var progress = await db.ReadingProgress.FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == bookId, cancellationToken);
        if (progress is null)
        {
            progress = new ReadingProgress(userId, bookId, now);
            db.ReadingProgress.Add(progress);
        }

        return progress;
    }
}
