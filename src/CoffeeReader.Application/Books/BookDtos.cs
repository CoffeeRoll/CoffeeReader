using CoffeeReader.Domain.Entities;

namespace CoffeeReader.Application.Books;

public sealed record AuthorRefDto(int Id, string Name, string SortName);

public sealed record SeriesRefDto(int Id, string Name);

public sealed record ProgressSummaryDto(double Percentage, bool IsFinished, DateTime UpdatedAt);

public sealed record BookSummaryDto(
    int Id,
    string Title,
    string SortTitle,
    IReadOnlyList<AuthorRefDto> Authors,
    SeriesRefDto? Series,
    double? SeriesIndex,
    bool HasCover,
    long CoverVersion,
    DateTime AddedAt,
    ProgressSummaryDto? Progress);

public sealed record BookDetailsDto(
    int Id,
    string Title,
    string SortTitle,
    IReadOnlyList<AuthorRefDto> Authors,
    SeriesRefDto? Series,
    double? SeriesIndex,
    bool HasCover,
    long CoverVersion,
    DateTime AddedAt,
    ProgressSummaryDto? Progress,
    string? Description,
    string? Publisher,
    string? Language,
    string? PublishedDate,
    string? Isbn,
    string FileName,
    long FileSize,
    BookFormat Format,
    int ChapterCount,
    bool MetadataLocked,
    IReadOnlyList<int> ShelfIds);

public sealed record UpdateBookRequest(
    string Title,
    IReadOnlyList<string>? Authors,
    string? Series,
    double? SeriesIndex,
    string? Description,
    string? Publisher,
    string? Language,
    string? PublishedDate,
    string? Isbn);

public sealed record BookDownload(string FullPath, string FileName, string ContentType);
