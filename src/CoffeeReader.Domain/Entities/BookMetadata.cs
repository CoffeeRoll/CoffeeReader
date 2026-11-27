namespace CoffeeReader.Domain.Entities;

/// <summary>
/// Descriptive information about a book, either read from the file or entered by an administrator.
/// </summary>
public sealed record BookMetadata(
    string Title,
    string? Description = null,
    string? Publisher = null,
    string? Language = null,
    string? PublishedDate = null,
    string? Isbn = null,
    string? Identifier = null);
