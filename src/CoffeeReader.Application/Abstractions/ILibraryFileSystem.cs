namespace CoffeeReader.Application.Abstractions;

/// <summary>
/// The library folder (the mounted books volume). All paths are relative to its root.
/// </summary>
public interface ILibraryFileSystem
{
    IReadOnlyList<LibraryFile> EnumerateFiles(IReadOnlyCollection<string> extensions);

    LibraryFile? GetFile(string relativePath);

    /// <summary>Resolves a relative path, refusing anything that escapes the library root.</summary>
    string GetFullPath(string relativePath);

    Task<string> ComputeHashAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>Stores an uploaded file and returns its relative path.</summary>
    Task<string> SaveUploadAsync(Stream content, string fileName, CancellationToken cancellationToken);

    void Delete(string relativePath);
}

public sealed record LibraryFile(string RelativePath, long SizeBytes, DateTime ModifiedAt);
