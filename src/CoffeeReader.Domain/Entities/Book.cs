using CoffeeReader.Domain.Common;

namespace CoffeeReader.Domain.Entities;

public sealed class Book
{
    public const int MaxTitleLength = 512;

    private readonly List<BookAuthor> _authors = [];
    private readonly List<BookChapter> _chapters = [];
    private readonly List<TocEntry> _tableOfContents = [];

    private Book()
    {
    }

    public Book(BookFile file, DateTime addedAt)
    {
        SetFile(file);
        AddedAt = addedAt;
        UpdatedAt = addedAt;
        Title = Path.GetFileNameWithoutExtension(file.RelativePath);
        SortTitle = TextNormalization.ToSortTitle(Title);
    }

    public int Id { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string SortTitle { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? Publisher { get; private set; }

    public string? Language { get; private set; }

    public string? PublishedDate { get; private set; }

    public string? Isbn { get; private set; }

    public string? Identifier { get; private set; }

    public int? SeriesId { get; private set; }

    public Series? Series { get; private set; }

    public double? SeriesIndex { get; private set; }

    /// <summary>Path relative to the library root, so the library can be remounted elsewhere.</summary>
    public string FilePath { get; private set; } = string.Empty;

    public long FileSize { get; private set; }

    public DateTime FileModifiedAt { get; private set; }

    /// <summary>SHA-256 of the file contents; used to recognise moved and duplicate files.</summary>
    public string FileHash { get; private set; } = string.Empty;

    public BookFormat Format { get; private set; }

    public BookFile File => new(FilePath, FileSize, FileModifiedAt, FileHash, Format);

    public bool HasCover { get; private set; }

    /// <summary>Path of the cover image inside the book container, if any.</summary>
    public string? CoverHref { get; private set; }

    /// <summary>Sum of all chapter lengths; the denominator for overall progress.</summary>
    public long TotalLength { get; private set; }

    /// <summary>When true the metadata was edited by hand and rescans won't overwrite it.</summary>
    public bool MetadataLocked { get; private set; }

    /// <summary>The file disappeared from the library folder. Kept so progress and shelves survive a move.</summary>
    public bool IsMissing { get; private set; }

    public DateTime AddedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyList<BookAuthor> Authors => _authors;

    public IReadOnlyList<BookChapter> Chapters => _chapters;

    public IReadOnlyList<TocEntry> TableOfContents => _tableOfContents;

    public void ApplyMetadata(BookMetadata metadata, IReadOnlyList<Author> authors, Series? series, double? seriesIndex, DateTime when)
    {
        var title = TextNormalization.CollapseWhitespace(metadata.Title).Trim();
        if (title.Length == 0)
        {
            throw new DomainException("A book needs a title.");
        }

        Title = title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
        SortTitle = TextNormalization.ToSortTitle(Title);
        Description = Clean(metadata.Description);
        Publisher = Clean(metadata.Publisher);
        Language = Clean(metadata.Language);
        PublishedDate = Clean(metadata.PublishedDate);
        Isbn = Clean(metadata.Isbn);
        Identifier = Clean(metadata.Identifier);

        _authors.Clear();
        for (var i = 0; i < authors.Count; i++)
        {
            if (_authors.All(a => a.Author.NormalizedName != authors[i].NormalizedName))
            {
                _authors.Add(new BookAuthor(authors[i], _authors.Count));
            }
        }

        Series = series;
        SeriesId = series?.Id;
        SeriesIndex = series is null ? null : seriesIndex;
        UpdatedAt = when;
    }

    /// <summary>
    /// Applies an administrator's edit and locks the metadata so that rescans keep it.
    /// </summary>
    public void EditMetadata(BookMetadata metadata, IReadOnlyList<Author> authors, Series? series, double? seriesIndex, DateTime when)
    {
        ApplyMetadata(metadata, authors, series, seriesIndex, when);
        MetadataLocked = true;
    }

    public void UnlockMetadata() => MetadataLocked = false;

    public void ReplaceStructure(IEnumerable<BookChapter> chapters, IEnumerable<TocEntry> tableOfContents, DateTime when)
    {
        _chapters.Clear();
        _chapters.AddRange(chapters.OrderBy(c => c.Index));
        if (_chapters.Count == 0)
        {
            throw new DomainException("A book needs at least one readable document.");
        }

        _tableOfContents.Clear();
        _tableOfContents.AddRange(tableOfContents.OrderBy(t => t.Order));
        TotalLength = _chapters.Sum(c => c.Length);
        UpdatedAt = when;
    }

    public void SetCover(string? coverHref)
    {
        CoverHref = string.IsNullOrWhiteSpace(coverHref) ? null : coverHref;
        HasCover = CoverHref is not null;
    }

    public void UpdateFile(BookFile file, DateTime when)
    {
        SetFile(file);
        IsMissing = false;
        UpdatedAt = when;
    }

    public void MarkMissing(DateTime when)
    {
        if (!IsMissing)
        {
            IsMissing = true;
            UpdatedAt = when;
        }
    }

    /// <summary>
    /// Converts a position inside a chapter into a fraction of the whole book.
    /// </summary>
    public double CalculatePercentage(int chapterIndex, double chapterProgress)
    {
        if (_chapters.Count == 0)
        {
            return 0;
        }

        chapterProgress = Math.Clamp(chapterProgress, 0, 1);
        chapterIndex = Math.Clamp(chapterIndex, 0, _chapters.Count - 1);
        if (TotalLength <= 0)
        {
            return Math.Clamp((chapterIndex + chapterProgress) / _chapters.Count, 0, 1);
        }

        var before = _chapters.Where(c => c.Index < chapterIndex).Sum(c => c.Length);
        var current = _chapters.First(c => c.Index == chapterIndex).Length;
        return Math.Clamp((before + (current * chapterProgress)) / TotalLength, 0, 1);
    }

    private void SetFile(BookFile file)
    {
        if (string.IsNullOrWhiteSpace(file.RelativePath))
        {
            throw new DomainException("A book file needs a path.");
        }

        FilePath = file.RelativePath;
        FileSize = file.SizeBytes;
        FileModifiedAt = file.ModifiedAt;
        FileHash = file.Hash;
        Format = file.Format;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
