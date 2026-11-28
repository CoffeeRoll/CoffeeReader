using CoffeeReader.Application.Books;
using CoffeeReader.Application.Library;
using CoffeeReader.Infrastructure.Persistence;
using CoffeeReader.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeReader.Tests.Application;

public class LibraryScanTests : IClassFixture<CoffeeReaderFactory>
{
    private readonly CoffeeReaderFactory _factory;

    public LibraryScanTests(CoffeeReaderFactory factory) => _factory = factory;

    [Fact]
    public async Task Scans_add_move_and_mark_missing_books_without_losing_identity()
    {
        _ = _factory.Server; // start the host (runs migrations)
        var folder = Path.Combine(_factory.LibraryPath, "Le Guin");
        var original = new EpubBuilder().Title("The Dispossessed").Author("Ursula K. Le Guin").WriteTo(folder, "dispossessed.epub");
        File.WriteAllText(Path.Combine(folder, "broken.epub"), "not a zip");
        Backdate(folder);

        var first = await ScanAsync();
        Assert.Equal(1, first.Added);
        Assert.Equal(1, first.Failed);

        int bookId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            bookId = (await db.Books.SingleAsync(b => b.Title == "The Dispossessed")).Id;
        }

        // Moving the file keeps the same book (and so its progress and shelves).
        var moved = Path.Combine(_factory.LibraryPath, "Sci-Fi", "dispossessed-renamed.epub");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(original, moved);
        Backdate(Path.GetDirectoryName(moved)!);
        var second = await ScanAsync();
        Assert.Equal(1, second.Restored);
        Assert.Equal(0, second.Added);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var book = await db.Books.SingleAsync(b => b.Id == bookId);
            Assert.Equal("Sci-Fi/dispossessed-renamed.epub", book.FilePath);
            Assert.False(book.IsMissing);
        }

        File.Delete(moved);
        var third = await ScanAsync();
        Assert.Equal(1, third.Missing);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True((await db.Books.SingleAsync(b => b.Id == bookId)).IsMissing);
        }
    }

    [Fact]
    public async Task Books_read_by_an_older_parser_are_read_again_keeping_edits()
    {
        _ = _factory.Server;
        var folder = Path.Combine(_factory.LibraryPath, "Manuals");
        new PdfBuilder().Title("Cordless Drill Guide").Author("Black & Decker").WriteTo(folder, "drill.pdf");
        new PdfBuilder().Title("Sander Guide").Author("Black & Decker").WriteTo(folder, "sander.pdf");
        Backdate(folder);
        Assert.Equal(2, (await ScanAsync()).Added);

        // Pretend both were imported by an older parser, and that an administrator edited one.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Books.Where(b => b.FilePath.StartsWith("Manuals/"))
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.ParserVersion, 0).SetProperty(b => b.Title, "Stale title"));
            await db.Books.Where(b => b.FilePath == "Manuals/sander.pdf").ExecuteUpdateAsync(s => s.SetProperty(b => b.MetadataLocked, true));
        }

        Assert.Equal(2, (await ScanAsync()).Updated);
        Assert.Equal(0, (await ScanAsync()).Updated); // only once

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var drill = await db.Books.Include(b => b.Authors).ThenInclude(a => a.Author).SingleAsync(b => b.FilePath == "Manuals/drill.pdf");
            Assert.Equal("Cordless Drill Guide", drill.Title);
            Assert.Equal("Black & Decker", Assert.Single(drill.Authors).Author.Name);
            var sander = await db.Books.SingleAsync(b => b.FilePath == "Manuals/sander.pdf");
            Assert.Equal("Stale title", sander.Title);
            Assert.True(drill.ParserVersion > 0 && sander.ParserVersion == drill.ParserVersion);
        }
    }

    private async Task<ScanSummary> ScanAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LibraryImporter>().ScanAsync(CancellationToken.None);
    }

    /// <summary>The scanner skips files that were just written (they may still be copying).</summary>
    private static void Backdate(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-5));
        }
    }
}
