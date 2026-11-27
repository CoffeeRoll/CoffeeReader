namespace CoffeeReader.Domain.Entities;

/// <summary>
/// One row of a flattened table of contents. Nesting is expressed through <see cref="Depth"/>.
/// </summary>
public sealed class TocEntry
{
    private TocEntry()
    {
    }

    public TocEntry(int order, string title, int depth, int chapterIndex, string? fragment)
    {
        Order = order;
        Title = title;
        Depth = Math.Max(0, depth);
        ChapterIndex = chapterIndex;
        Fragment = string.IsNullOrWhiteSpace(fragment) ? null : fragment;
    }

    public int Id { get; private set; }

    public int BookId { get; private set; }

    public int Order { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public int Depth { get; private set; }

    public int ChapterIndex { get; private set; }

    public string? Fragment { get; private set; }
}
