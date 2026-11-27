namespace CoffeeReader.Application.Abstractions;

/// <summary>
/// Serves renderable content out of an ebook file.
/// </summary>
public interface IEbookContentProvider
{
    /// <summary>Returns a sanitized, render-ready chapter document.</summary>
    Task<ChapterDocument> GetChapterAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken);

    /// <summary>Returns the plain text of a chapter exactly as the reader will display it.</summary>
    Task<string> GetChapterTextAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken);

    /// <summary>Opens an embedded resource (image, stylesheet, font). Returns null when it doesn't exist.</summary>
    Task<BookResource?> OpenResourceAsync(string fullPath, string resourcePath, CancellationToken cancellationToken);
}

public sealed record ChapterDocument(
    string Href,
    string BodyHtml,
    IReadOnlyList<string> Stylesheets,
    IReadOnlyList<string> InlineStyles,
    string? BodyId,
    string? BodyClass,
    string? BodyStyle,
    string? Language,
    string? Direction);

public sealed record BookResource(byte[] Content, string ContentType);
