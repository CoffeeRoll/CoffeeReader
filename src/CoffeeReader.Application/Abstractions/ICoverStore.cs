namespace CoffeeReader.Application.Abstractions;

/// <summary>
/// Stores resized cover images so the library grid stays light on mobile data.
/// </summary>
public interface ICoverStore
{
    /// <summary>Creates a thumbnail from the image. Returns false when the image can't be decoded.</summary>
    Task<bool> SaveAsync(int bookId, byte[] image, CancellationToken cancellationToken);

    CoverImage? Open(int bookId);

    void Delete(int bookId);
}

public sealed record CoverImage(Stream Content, string ContentType, DateTime LastModified);
