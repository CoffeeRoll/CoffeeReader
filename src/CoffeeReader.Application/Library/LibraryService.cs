using CoffeeReader.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CoffeeReader.Application.Library;

public sealed class LibraryService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    LibraryImporter importer,
    LibraryScanState state,
    ILibraryScanTrigger scanTrigger)
{
    public IReadOnlyCollection<string> SupportedExtensions => importer.SupportedExtensions;

    public async Task<LibraryStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        currentUser.RequireUserId();
        var bookCount = await db.Books.CountAsync(b => !b.IsMissing, cancellationToken);
        var missingCount = await db.Books.CountAsync(b => b.IsMissing, cancellationToken);
        return new LibraryStatusDto(
            state.IsScanning,
            state.LastStartedAt,
            state.LastCompletedAt,
            state.LastSummary,
            state.LastError,
            currentUser.IsAdmin ? state.Issues : [],
            bookCount,
            missingCount);
    }

    public void RequestScan()
    {
        currentUser.RequireAdmin();
        scanTrigger.RequestScan("requested by an administrator");
    }

    public async Task<UploadResultDto> UploadAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        currentUser.RequireUserId();
        return await importer.ImportUploadAsync(content, Path.GetFileName(fileName), cancellationToken);
    }

    /// <summary>Forgets books whose files have been gone since the last scan.</summary>
    public async Task<int> RemoveMissingAsync(CancellationToken cancellationToken)
    {
        currentUser.RequireAdmin();
        return await db.Books.Where(b => b.IsMissing).ExecuteDeleteAsync(cancellationToken);
    }
}
