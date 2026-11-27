namespace CoffeeReader.Domain.Entities;

/// <summary>
/// A document in the book's reading order (an EPUB spine item).
/// </summary>
public sealed class BookChapter
{
    private BookChapter()
    {
    }

    public BookChapter(int index, string href, string? title, long length, bool isLinear)
    {
        Index = index;
        Href = href;
        Title = title;
        Length = Math.Max(0, length);
        IsLinear = isLinear;
    }

    public int Id { get; private set; }

    public int BookId { get; private set; }

    /// <summary>Zero-based position in the reading order.</summary>
    public int Index { get; private set; }

    /// <summary>Path of the document inside the book container.</summary>
    public string Href { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    /// <summary>Relative size of the chapter, used to compute overall progress.</summary>
    public long Length { get; private set; }

    public bool IsLinear { get; private set; }
}
