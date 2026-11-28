using System.Net;
using CoffeeReader.Application.Auth;
using CoffeeReader.Application.Books;
using CoffeeReader.Application.Library;
using CoffeeReader.Application.Reading;
using CoffeeReader.Application.Shelves;
using CoffeeReader.Application.Users;
using CoffeeReader.Domain.Entities;
using CoffeeReader.Tests.Support;

namespace CoffeeReader.Tests.Api;

public class ApiFlowTests : IClassFixture<CoffeeReaderFactory>
{
    private readonly CoffeeReaderFactory _factory;

    public ApiFlowTests(CoffeeReaderFactory factory) => _factory = factory;

    [Fact]
    public async Task Full_reading_flow_works_for_multiple_users()
    {
        var admin = _factory.CreateApiClient();

        // First run: the app asks for an administrator.
        var status = await (await admin.GetAsync("/api/auth/status")).ReadAsync<AuthStatusDto>();
        Assert.True(status.SetupRequired);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/books")).StatusCode);

        var me = await (await admin.PostJsonAsync("/api/auth/setup", new SetupRequest("ada", "Ada", "correct horse battery"))).ReadAsync<UserDto>();
        Assert.Equal(UserRole.Admin, me.Role);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJsonAsync("/api/auth/setup", new SetupRequest("eve", null, "password123"))).StatusCode);

        // Upload a book.
        var epub = new EpubBuilder()
            .Title("The Left Hand of Darkness")
            .Author("Ursula K. Le Guin")
            .Series("Hainish Cycle", "4")
            .Chapter("A Parade in Erhenrang", "<p>I'll make my report as if I told a story. The truth is a matter of the imagination.</p>")
            .Chapter("The Place Inside the Blizzard", "<p>The soundest fact may fail or prevail in the style of its telling. Imagination, again.</p>")
            .Build();
        var uploads = await (await admin.UploadAsync(epub, "left-hand.epub")).ReadAsync<List<UploadResultDto>>();
        var upload = Assert.Single(uploads);
        Assert.Equal(UploadStatus.Added, upload.Status);
        Assert.True(File.Exists(Path.Combine(_factory.LibraryPath, "Uploads", "left-hand.epub")));

        var duplicate = await (await admin.UploadAsync(epub, "copy.epub")).ReadAsync<List<UploadResultDto>>();
        Assert.Equal(UploadStatus.Duplicate, Assert.Single(duplicate).Status);
        Assert.False(File.Exists(Path.Combine(_factory.LibraryPath, "Uploads", "copy.epub")));

        var notABook = await (await admin.UploadAsync("hello"u8.ToArray(), "notes.epub")).ReadAsync<List<UploadResultDto>>();
        Assert.Equal(UploadStatus.Failed, Assert.Single(notABook).Status);

        var bookId = upload.BookId!.Value;
        var books = await (await admin.GetAsync("/api/books")).ReadAsync<List<BookSummaryDto>>();
        var summary = Assert.Single(books);
        Assert.Equal("Hainish Cycle", summary.Series?.Name);
        Assert.Equal(4, summary.SeriesIndex);
        Assert.True(summary.HasCover);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/books/{bookId}/cover?v={summary.CoverVersion}")).StatusCode);

        // Read it.
        var manifest = await (await admin.GetAsync($"/api/books/{bookId}/manifest")).ReadAsync<ReaderManifestDto>();
        Assert.Equal(2, manifest.Chapters.Count);
        Assert.Equal("The Place Inside the Blizzard", manifest.Chapters[1].Title);

        var chapter = await (await admin.GetAsync($"/api/books/{bookId}/chapters/0")).ReadAsync<ChapterDto>();
        Assert.Contains("I'll make my report", chapter.Html);
        Assert.Equal(["OEBPS/Styles/book.css"], chapter.Stylesheets);

        var css = await admin.GetAsync($"/api/books/{bookId}/files/OEBPS/Styles/book.css");
        Assert.Equal("text/css", css.Content.Headers.ContentType?.MediaType);
        Assert.Contains("sandbox", css.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/books/{bookId}/files/OEBPS/Text/ch1.xhtml")).StatusCode);

        var search = await (await admin.GetAsync($"/api/books/{bookId}/search?q=IMAGINATION")).ReadAsync<SearchResponseDto>();
        Assert.Equal(2, search.TotalMatches);
        Assert.Equal([0, 1], search.Hits.Select(h => h.ChapterIndex));
        Assert.Equal("imagination", search.Hits[0].Match);

        var progress = await (await admin.PutJsonAsync($"/api/books/{bookId}/progress", new SaveProgressRequest(1, 0.5))).ReadAsync<ProgressDto>();
        Assert.InRange(progress.Percentage, 0.5, 1);

        // A second user has their own progress and shelves.
        await admin.PostJsonAsync("/api/users", new CreateUserRequest("grace", "Grace", "hopper-1906", UserRole.User));
        var grace = _factory.CreateApiClient();
        await (await grace.PostJsonAsync("/api/auth/login", new LoginRequest("GRACE", "hopper-1906"))).ReadAsync<UserDto>();
        Assert.Equal(HttpStatusCode.NoContent, (await grace.GetAsync($"/api/books/{bookId}/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await grace.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await grace.DeleteAsync($"/api/books/{bookId}")).StatusCode);

        var shelf = await (await admin.PostJsonAsync("/api/shelves", new CreateShelfRequest("Favourites"))).ReadAsync<ShelfDto>();
        await (await admin.PostJsonAsync($"/api/shelves/{shelf.Id}/books", new AddShelfBookRequest(bookId, null))).ReadAsync<ShelfDto>();
        Assert.Empty(await (await grace.GetAsync("/api/shelves")).ReadAsync<List<ShelfDto>>());
        Assert.Equal(HttpStatusCode.NotFound, (await grace.DeleteAsync($"/api/shelves/{shelf.Id}")).StatusCode);

        var details = await (await admin.GetAsync($"/api/books/{bookId}")).ReadAsync<BookDetailsDto>();
        Assert.Equal([shelf.Id], details.ShelfIds);

        // Changing a password signs out the account's other sessions.
        await grace.PostJsonAsync("/api/auth/change-password", new ChangePasswordRequest("hopper-1906", "nanosecond-wire"));
        Assert.Equal(HttpStatusCode.OK, (await grace.GetAsync("/api/books")).StatusCode);
        var graceElsewhere = _factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await graceElsewhere.PostJsonAsync("/api/auth/login", new LoginRequest("grace", "hopper-1906"))).StatusCode);
    }

    [Fact]
    public async Task State_changing_requests_need_the_csrf_header()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_api_routes_are_404_not_the_spa()
    {
        var client = _factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/nope")).StatusCode);
    }
}
