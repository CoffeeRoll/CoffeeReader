using System.Diagnostics;
using CoffeeReader.Application.Abstractions;
using CoffeeReader.Application.Books;
using CoffeeReader.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CoffeeReader.Application.Library;

/// <summary>
/// Keeps the database in step with the files in the library folder.
/// </summary>
/// <remarks>
/// The folder is the source of truth for <em>which</em> books exist; the database owns everything else
/// (metadata edits, shelves, progress). Files are matched by content hash so renaming or moving a file
/// keeps its history.
/// </remarks>
public sealed class LibraryImporter(
    IApplicationDbContext db,
    ILibraryFileSystem files,
    IEnumerable<IEbookParser> parsers,
    ICoverStore covers,
    LibraryScanState state,
    LibraryLock libraryLock,
    TimeProvider clock,
    ILogger<LibraryImporter> logger)
{
    /// <summary>Files modified more recently than this may still be copying in.</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(3);

    private readonly IReadOnlyList<IEbookParser> _parsers = parsers.ToList();

    public IReadOnlyCollection<string> SupportedExtensions =>
        _parsers.SelectMany(p => p.Extensions).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<ScanSummary> ScanAsync(CancellationToken cancellationToken)
    {
        using var _ = await libraryLock.AcquireAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        state.Begin(Now);
        try
        {
            var (summary, issues) = await ScanCoreAsync(stopwatch, cancellationToken);
            state.Complete(Now, summary, issues);
            logger.LogInformation(
                "Library scan finished in {Seconds:0.0}s: {Added} added, {Updated} updated, {Restored} restored, {Missing} missing, {Failed} failed",
                summary.DurationSeconds, summary.Added, summary.Updated, summary.Restored, summary.Missing, summary.Failed);
            return summary;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Library scan failed");
            state.Fail(Now, ex.Message);
            throw;
        }
        catch (OperationCanceledException)
        {
            state.Fail(Now, "The scan was cancelled.");
            throw;
        }
    }

    public async Task<UploadResultDto> ImportUploadAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName);
        if (FindParser(extension) is null)
        {
            return new UploadResultDto(fileName, UploadStatus.Failed, null, null,
                $"Only {string.Join(", ", SupportedExtensions)} files can be added.");
        }

        using var _ = await libraryLock.AcquireAsync(cancellationToken);
        string relativePath;
        try
        {
            relativePath = await files.SaveUploadAsync(content, fileName, cancellationToken);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.LogError(ex, "Couldn't save an upload to the library folder");
            return new UploadResultDto(fileName, UploadStatus.Failed, null, null,
                "The library folder isn't writable, so uploads can't be saved. Check the books volume's permissions.");
        }

        try
        {
            var file = files.GetFile(relativePath) ?? throw new IOException("The uploaded file could not be read back.");
            var hash = await files.ComputeHashAsync(relativePath, cancellationToken);
            var existing = await db.Books.FirstOrDefaultAsync(b => b.FileHash == hash, cancellationToken);
            if (existing is not null && !existing.IsMissing)
            {
                files.Delete(relativePath);
                return new UploadResultDto(fileName, UploadStatus.Duplicate, existing.Id, existing.Title, "This book is already in the library.");
            }

            if (existing is not null)
            {
                existing.UpdateFile(new BookFile(relativePath, file.SizeBytes, file.ModifiedAt, hash, existing.Format), Now);
                await db.SaveChangesAsync(cancellationToken);
                return new UploadResultDto(fileName, UploadStatus.Added, existing.Id, existing.Title, null);
            }

            var book = await ImportNewAsync(file, hash, cancellationToken);
            return new UploadResultDto(fileName, UploadStatus.Added, book.Id, book.Title, null);
        }
        catch (EbookFormatException ex)
        {
            files.Delete(relativePath);
            return new UploadResultDto(fileName, UploadStatus.Failed, null, null, ex.Message);
        }
    }

    private async Task<(ScanSummary Summary, IReadOnlyList<ScanIssue> Issues)> ScanCoreAsync(Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        int added = 0, updated = 0, restored = 0, missing = 0;
        var issues = new List<ScanIssue>();
        var onDisk = files.EnumerateFiles(SupportedExtensions);
        var books = await db.Books.ToListAsync(cancellationToken);
        var byPath = books.ToDictionary(b => b.FilePath, StringComparer.Ordinal);
        var diskPaths = onDisk.Select(f => f.RelativePath).ToHashSet(StringComparer.Ordinal);
        var knownHashes = books.Select(b => b.FileHash).ToHashSet(StringComparer.Ordinal);
        var vanished = books.Where(b => !diskPaths.Contains(b.FilePath)).ToList();

        foreach (var file in onDisk.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Now - file.ModifiedAt < SettleTime)
            {
                continue;
            }

            try
            {
                if (byPath.TryGetValue(file.RelativePath, out var existing))
                {
                    if (existing.FileSize == file.SizeBytes && existing.FileModifiedAt == file.ModifiedAt && !existing.IsMissing)
                    {
                        continue;
                    }

                    var wasMissing = existing.IsMissing;
                    var hash = await files.ComputeHashAsync(file.RelativePath, cancellationToken);
                    if (hash == existing.FileHash)
                    {
                        existing.UpdateFile(existing.File with { SizeBytes = file.SizeBytes, ModifiedAt = file.ModifiedAt }, Now);
                        restored += wasMissing ? 1 : 0;
                    }
                    else
                    {
                        await ReimportAsync(existing, file, hash, cancellationToken);
                        knownHashes.Add(hash);
                        updated++;
                    }

                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                var newHash = await files.ComputeHashAsync(file.RelativePath, cancellationToken);
                var moved = vanished.FirstOrDefault(b => b.FileHash == newHash);
                if (moved is not null)
                {
                    moved.UpdateFile(new BookFile(file.RelativePath, file.SizeBytes, file.ModifiedAt, newHash, moved.Format), Now);
                    vanished.Remove(moved);
                    await db.SaveChangesAsync(cancellationToken);
                    restored++;
                    continue;
                }

                if (knownHashes.Contains(newHash))
                {
                    var original = books.First(b => b.FileHash == newHash);
                    issues.Add(new ScanIssue(file.RelativePath, $"Skipped: it's a copy of \"{original.Title}\" ({original.FilePath})."));
                    continue;
                }

                var book = await ImportNewAsync(file, newHash, cancellationToken);
                books.Add(book);
                knownHashes.Add(newHash);
                added++;
            }
            catch (EbookFormatException ex)
            {
                logger.LogWarning("Couldn't read {Path}: {Message}", file.RelativePath, ex.Message);
                issues.Add(new ScanIssue(file.RelativePath, ex.Message));
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Couldn't access {Path}", file.RelativePath);
                issues.Add(new ScanIssue(file.RelativePath, ex.Message));
            }
        }

        foreach (var book in vanished.Where(b => !b.IsMissing))
        {
            book.MarkMissing(Now);
            missing++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await new CatalogResolver(db).RemoveOrphansAsync(cancellationToken);

        var summary = new ScanSummary(added, updated, restored, missing, issues.Count, Math.Round(stopwatch.Elapsed.TotalSeconds, 2));
        return (summary, issues);
    }

    private async Task<Book> ImportNewAsync(LibraryFile file, string hash, CancellationToken cancellationToken)
    {
        var parser = FindParser(Path.GetExtension(file.RelativePath))
            ?? throw new EbookFormatException("Unsupported file type.");
        var parsed = await parser.ParseAsync(files.GetFullPath(file.RelativePath), cancellationToken);

        var book = new Book(new BookFile(file.RelativePath, file.SizeBytes, file.ModifiedAt, hash, parser.Format), Now);
        await ApplyAsync(book, parsed, applyMetadata: true, cancellationToken);
        db.Books.Add(book);
        await db.SaveChangesAsync(cancellationToken);
        await SaveCoverAsync(book, parsed, cancellationToken);
        logger.LogInformation("Added \"{Title}\" from {Path}", book.Title, file.RelativePath);
        return book;
    }

    private async Task ReimportAsync(Book book, LibraryFile file, string hash, CancellationToken cancellationToken)
    {
        var parser = FindParser(Path.GetExtension(file.RelativePath))
            ?? throw new EbookFormatException("Unsupported file type.");
        var parsed = await parser.ParseAsync(files.GetFullPath(file.RelativePath), cancellationToken);

        await db.Books.Entry(book).Collection(b => b.Chapters).LoadAsync(cancellationToken);
        await db.Books.Entry(book).Collection(b => b.TableOfContents).LoadAsync(cancellationToken);
        await db.Books.Entry(book).Collection(b => b.Authors).LoadAsync(cancellationToken);
        book.UpdateFile(new BookFile(file.RelativePath, file.SizeBytes, file.ModifiedAt, hash, parser.Format), Now);
        await ApplyAsync(book, parsed, applyMetadata: !book.MetadataLocked, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await SaveCoverAsync(book, parsed, cancellationToken);
    }

    private async Task ApplyAsync(Book book, ParsedEbook parsed, bool applyMetadata, CancellationToken cancellationToken)
    {
        if (applyMetadata)
        {
            var resolver = new CatalogResolver(db);
            var authors = await resolver.ResolveAuthorsAsync(parsed.Authors.Select(a => (a.Name, a.SortName)), cancellationToken);
            var series = await resolver.ResolveSeriesAsync(parsed.SeriesName, cancellationToken);
            book.ApplyMetadata(parsed.Metadata, authors, series, parsed.SeriesIndex, Now);
        }

        var titles = ChapterTitles(parsed);
        var chapters = parsed.Chapters.Select((c, i) => new BookChapter(i, c.Href, titles[i], c.Length, c.IsLinear));
        var toc = parsed.TableOfContents.Select((t, i) => new TocEntry(i, t.Title, t.Depth, t.ChapterIndex, t.Fragment));
        book.ReplaceStructure(chapters, toc, Now);
    }

    private async Task SaveCoverAsync(Book book, ParsedEbook parsed, CancellationToken cancellationToken)
    {
        var saved = false;
        if (parsed.CoverImage is { Length: > 0 } image)
        {
            try
            {
                saved = await covers.SaveAsync(book.Id, image, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Couldn't create a cover thumbnail for \"{Title}\"", book.Title);
            }
        }

        if (!saved)
        {
            covers.Delete(book.Id);
        }

        book.SetCover(saved ? parsed.CoverHref : null);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Names each reading-order document after the table of contents entry that covers it,
    /// so running heads and search results can say which chapter you're in.
    /// </summary>
    private static string?[] ChapterTitles(ParsedEbook parsed)
    {
        var titles = new string?[parsed.Chapters.Count];
        foreach (var entry in parsed.TableOfContents)
        {
            if (entry.ChapterIndex >= 0 && entry.ChapterIndex < titles.Length && titles[entry.ChapterIndex] is null)
            {
                titles[entry.ChapterIndex] = entry.Title;
            }
        }

        for (var i = 1; i < titles.Length; i++)
        {
            titles[i] ??= titles[i - 1];
        }

        return titles;
    }

    private IEbookParser? FindParser(string extension) =>
        _parsers.FirstOrDefault(p => p.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
