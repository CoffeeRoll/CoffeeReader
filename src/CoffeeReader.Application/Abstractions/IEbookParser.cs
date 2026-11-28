using CoffeeReader.Domain.Entities;

namespace CoffeeReader.Application.Abstractions;

/// <summary>
/// Reads the structure and metadata of an ebook file.
/// </summary>
public interface IEbookParser
{
    BookFormat Format { get; }

    IReadOnlyCollection<string> Extensions { get; }

    /// <summary>
    /// Raise this when a change to the parser should be applied to books already in the library:
    /// books read by an older version are read again on the next scan (edited details are kept).
    /// </summary>
    int MetadataVersion { get; }

    Task<ParsedEbook> ParseAsync(string fullPath, CancellationToken cancellationToken);
}

public sealed record ParsedEbook(
    BookMetadata Metadata,
    IReadOnlyList<ParsedAuthor> Authors,
    string? SeriesName,
    double? SeriesIndex,
    IReadOnlyList<ParsedChapter> Chapters,
    IReadOnlyList<ParsedTocEntry> TableOfContents,
    string? CoverHref,
    byte[]? CoverImage);

public sealed record ParsedAuthor(string Name, string? SortName);

public sealed record ParsedChapter(string Href, long Length, bool IsLinear);

public sealed record ParsedTocEntry(string Title, int Depth, int ChapterIndex, string? Fragment);

/// <summary>
/// Thrown when a file is not a readable ebook.
/// </summary>
public sealed class EbookFormatException(string message, Exception? inner = null) : Exception(message, inner);
