namespace CoffeeReader.Domain.Entities;

/// <summary>
/// Where a user is in a book.
/// </summary>
public sealed class ReadingProgress
{
    /// <summary>Progress at or above this fraction marks a book as finished automatically.</summary>
    public const double FinishedThreshold = 0.995;

    private ReadingProgress()
    {
    }

    public ReadingProgress(int userId, int bookId, DateTime startedAt)
    {
        UserId = userId;
        BookId = bookId;
        StartedAt = startedAt;
        UpdatedAt = startedAt;
    }

    public int Id { get; private set; }

    public int UserId { get; private set; }

    public int BookId { get; private set; }

    public int ChapterIndex { get; private set; }

    /// <summary>Fraction (0..1) of the current chapter that has been read.</summary>
    public double ChapterProgress { get; private set; }

    /// <summary>Fraction (0..1) of the entire book that has been read.</summary>
    public double Percentage { get; private set; }

    public bool IsFinished { get; private set; }

    public DateTime StartedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public DateTime? FinishedAt { get; private set; }

    public void Update(int chapterIndex, double chapterProgress, double percentage, DateTime when)
    {
        ChapterIndex = Math.Max(0, chapterIndex);
        ChapterProgress = Math.Clamp(chapterProgress, 0, 1);
        Percentage = Math.Clamp(percentage, 0, 1);
        UpdatedAt = when;
        if (Percentage >= FinishedThreshold)
        {
            MarkFinished(when);
        }
    }

    public void MarkFinished(DateTime when)
    {
        if (!IsFinished)
        {
            IsFinished = true;
            FinishedAt = when;
        }

        UpdatedAt = when;
    }

    public void MarkUnfinished(DateTime when)
    {
        IsFinished = false;
        FinishedAt = null;
        UpdatedAt = when;
    }
}
