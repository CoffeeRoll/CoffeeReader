namespace CoffeeReader.Domain.Entities;

/// <summary>
/// Where a book lives on disk. Paths are relative to the library root so the library can be remounted.
/// </summary>
public sealed record BookFile(string RelativePath, long SizeBytes, DateTime ModifiedAt, string Hash, BookFormat Format);
