namespace CoffeeReader.Application.Reading;

public sealed record ChapterInfoDto(int Index, string Href, string? Title, long Length, bool IsLinear);

public sealed record TocEntryDto(string Title, int Depth, int ChapterIndex, string? Fragment);

public sealed record ProgressDto(
    int ChapterIndex,
    double ChapterProgress,
    double Percentage,
    bool IsFinished,
    DateTime UpdatedAt,
    DateTime? FinishedAt);

public sealed record ReaderManifestDto(
    int BookId,
    string Title,
    IReadOnlyList<string> Authors,
    string? Language,
    IReadOnlyList<ChapterInfoDto> Chapters,
    IReadOnlyList<TocEntryDto> TableOfContents,
    long TotalLength,
    ProgressDto? Progress);

public sealed record ChapterDto(
    int Index,
    string Href,
    string Html,
    IReadOnlyList<string> Stylesheets,
    IReadOnlyList<string> InlineStyles,
    string? BodyId,
    string? BodyClass,
    string? BodyStyle,
    string? Language,
    string? Direction);

public sealed record SearchHitDto(
    int ChapterIndex,
    string? ChapterTitle,
    int Occurrence,
    string Before,
    string Match,
    string After,
    double Percentage);

public sealed record SearchResponseDto(string Query, int TotalMatches, bool Truncated, IReadOnlyList<SearchHitDto> Hits);

public sealed record SaveProgressRequest(int ChapterIndex, double ChapterProgress);

public sealed record SetFinishedRequest(bool Finished);
