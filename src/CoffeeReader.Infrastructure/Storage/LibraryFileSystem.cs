using System.Security.Cryptography;
using CoffeeReader.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeeReader.Infrastructure.Storage;

public sealed class LibraryFileSystem(IOptions<StorageOptions> options) : ILibraryFileSystem
{
    private readonly string _root = Path.GetFullPath(options.Value.LibraryPath);
    private readonly string _uploadFolder = options.Value.UploadFolder;

    public IReadOnlyList<LibraryFile> EnumerateFiles(IReadOnlyCollection<string> extensions)
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            ReturnSpecialDirectories = false,
        };

        return new DirectoryInfo(_root)
            .EnumerateFiles("*", enumeration)
            .Where(f => extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
            .Where(f => !f.Name.StartsWith('.') && !IsInHiddenDirectory(f))
            .Select(ToLibraryFile)
            .ToList();
    }

    public LibraryFile? GetFile(string relativePath)
    {
        var info = new FileInfo(GetFullPath(relativePath));
        return info.Exists ? ToLibraryFile(info) : null;
    }

    public string GetFullPath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("That path is outside the library folder.");
        }

        return full;
    }

    public async Task<string> ComputeHashAsync(string relativePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(GetFullPath(relativePath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    public async Task<string> SaveUploadAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        var directory = GetFullPath(_uploadFolder);
        Directory.CreateDirectory(directory);

        var safeName = SafeFileName(fileName);
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName).ToLowerInvariant();
        var temporary = Path.Combine(directory, $".upload-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(output, cancellationToken);
            }

            for (var attempt = 0; ; attempt++)
            {
                var candidate = Path.Combine(directory, attempt == 0 ? $"{stem}{extension}" : $"{stem} ({attempt}){extension}");
                try
                {
                    File.Move(temporary, candidate, overwrite: false);
                    return Path.GetRelativePath(_root, candidate).Replace('\\', '/');
                }
                catch (IOException) when (File.Exists(candidate) && attempt < 1000)
                {
                }
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public void Delete(string relativePath)
    {
        var full = GetFullPath(relativePath);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }

    internal static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimStart('.');
        if (cleaned.Length > 180)
        {
            var extension = Path.GetExtension(cleaned);
            cleaned = cleaned[..(180 - extension.Length)] + extension;
        }

        return string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(cleaned)) ? $"book{Path.GetExtension(cleaned)}" : cleaned;
    }

    private LibraryFile ToLibraryFile(FileInfo file) =>
        new(Path.GetRelativePath(_root, file.FullName).Replace('\\', '/'),
            file.Length,
            TruncateToSeconds(file.LastWriteTimeUtc));

    private bool IsInHiddenDirectory(FileInfo file)
    {
        for (var directory = file.Directory; directory is not null && directory.FullName.Length > _root.Length; directory = directory.Parent)
        {
            if (directory.Name.StartsWith('.') || directory.Name.Equals("@eaDir", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // SQLite round-trips DateTime with limited precision; compare at whole seconds.
    private static DateTime TruncateToSeconds(DateTime value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
}
