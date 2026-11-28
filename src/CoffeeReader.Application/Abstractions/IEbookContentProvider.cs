using CoffeeReader.Domain.Entities;

namespace CoffeeReader.Application.Abstractions;

/// <summary>
/// Serves renderable content out of an ebook file.
/// </summary>
public interface IEbookContentProvider
{
    BookFormat Format { get; }

    /// <summary>Returns a sanitized, render-ready chapter document.</summary>
    Task<ChapterDocument> GetChapterAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken);

    /// <summary>Returns the plain text of a chapter exactly as the reader will display it.</summary>
    Task<ChapterText> GetChapterTextAsync(string fullPath, string fileHash, string chapterHref, CancellationToken cancellationToken);

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

/// <summary>
/// A chapter's text (its DOM <c>textContent</c>) plus the offsets where block elements begin or end,
/// so snippets can show paragraph breaks as spaces without shifting match positions.
/// </summary>
public sealed record ChapterText(string Text, IReadOnlyList<int> BlockBreaks);

public sealed record BookResource(byte[] Content, string ContentType);
