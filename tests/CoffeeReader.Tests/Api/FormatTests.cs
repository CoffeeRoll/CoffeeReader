using System.Net;
using System.Net.Http.Headers;
using CoffeeReader.Application.Auth;
using CoffeeReader.Application.Books;
using CoffeeReader.Application.Library;
using CoffeeReader.Application.Reading;
using CoffeeReader.Domain.Entities;
using CoffeeReader.Tests.Support;

namespace CoffeeReader.Tests.Api;

public class FormatTests : IClassFixture<CoffeeReaderFactory>
{
    private readonly CoffeeReaderFactory _factory;

    public FormatTests(CoffeeReaderFactory factory) => _factory = factory;

    [Fact]
    public async Task Mobi_and_pdf_books_can_be_uploaded_read_and_searched()
    {
        var client = _factory.CreateApiClient();
        await (await client.PostJsonAsync("/api/auth/setup", new SetupRequest("reader", null, "formats-are-fun"))).ReadAsync<object>();

        var formats = await (await client.GetAsync("/api/library/formats")).Content.ReadAsStringAsync();
        Assert.Contains(".mobi", formats);
        Assert.Contains(".pdf", formats);

        var mobi = new MobiBuilder().Title("Salt and Signal").Author("Ada Fenwick")
            .Chapter("Arrival", "<p>The keeper climbed the stairs to the lantern room.</p>")
            .Chapter("Storm", "<p>All night the lantern burned against the storm.</p>")
            .Build();
        var pdf = new PdfBuilder().Title("Harbour Charts")
            .Page("Harbour Charts", "Soundings and channels.")
            .Page("North Channel", "The lantern on the breakwater marks the channel.")
            .Bookmark("North Channel", 1)
            .Build();

        var mobiUpload = Assert.Single(await (await client.UploadAsync(mobi, "salt.mobi")).ReadAsync<List<UploadResultDto>>());
        var pdfUpload = Assert.Single(await (await client.UploadAsync(pdf, "charts.pdf")).ReadAsync<List<UploadResultDto>>());
        Assert.Equal(UploadStatus.Added, mobiUpload.Status);
        Assert.Equal(UploadStatus.Added, pdfUpload.Status);

        var books = await (await client.GetAsync("/api/books")).ReadAsync<List<BookSummaryDto>>();
        Assert.Equal(BookFormat.Mobi, books.Single(b => b.Id == mobiUpload.BookId).Format);
        Assert.Equal(BookFormat.Pdf, books.Single(b => b.Id == pdfUpload.BookId).Format);
        Assert.All(books, b => Assert.True(b.HasCover));

        // MOBI reads through the HTML reader.
        var mobiManifest = await (await client.GetAsync($"/api/books/{mobiUpload.BookId}/manifest")).ReadAsync<ReaderManifestDto>();
        Assert.Equal(BookFormat.Mobi, mobiManifest.Format);
        Assert.Equal(["Arrival", "Storm"], mobiManifest.TableOfContents.Select(t => t.Title));
        var chapter = await (await client.GetAsync($"/api/books/{mobiUpload.BookId}/chapters/{mobiManifest.TableOfContents[1].ChapterIndex}")).ReadAsync<ChapterDto>();
        Assert.Contains("All night the lantern burned", chapter.Html);
        var mobiSearch = await (await client.GetAsync($"/api/books/{mobiUpload.BookId}/search?q=lantern")).ReadAsync<SearchResponseDto>();
        Assert.Equal(2, mobiSearch.TotalMatches);

        // PDF pages are drawn by the browser; the server streams the file and searches its text.
        var pdfManifest = await (await client.GetAsync($"/api/books/{pdfUpload.BookId}/manifest")).ReadAsync<ReaderManifestDto>();
        Assert.Equal(BookFormat.Pdf, pdfManifest.Format);
        Assert.Equal(2, pdfManifest.Chapters.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/books/{pdfUpload.BookId}/chapters/0")).StatusCode);

        var pdfSearch = await (await client.GetAsync($"/api/books/{pdfUpload.BookId}/search?q=lantern")).ReadAsync<SearchResponseDto>();
        var hit = Assert.Single(pdfSearch.Hits);
        Assert.Equal(1, hit.ChapterIndex);
        Assert.Equal("North Channel", hit.ChapterTitle);

        var range = new HttpRequestMessage(HttpMethod.Get, $"/api/books/{pdfUpload.BookId}/file");
        range.Headers.Range = new RangeHeaderValue(0, 7);
        var partial = await client.SendAsync(range);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("application/pdf", partial.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF-1.7", await partial.Content.ReadAsStringAsync());

        // Progress for a PDF is per page.
        var progress = await (await client.PutJsonAsync($"/api/books/{pdfUpload.BookId}/progress", new SaveProgressRequest(1, 1))).ReadAsync<ProgressDto>();
        Assert.Equal(1, progress.Percentage);
        Assert.True(progress.IsFinished);
    }
}
