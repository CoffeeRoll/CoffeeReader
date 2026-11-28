using CoffeeReader.Application.Abstractions;
using CoffeeReader.Infrastructure.Ebooks.Pdf;
using CoffeeReader.Tests.Support;

namespace CoffeeReader.Tests.Ebooks;

public class PdfTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private static PdfBuilder SampleBook() => new PdfBuilder()
        .Title("Field Notes on Tides")
        .Author("Ines Calloway; Mark Ode")
        .Subject("Observations from a year on the coast.")
        .Page("Field Notes on Tides", "A year of observations.")
        .Page("Spring", "The spring tides came early that year,", "and the estuary filled with silt.")
        .Page("Summer", "Long evenings; the water warm and still.", "We measured the neap tides every morning.")
        .Page("Autumn", "Storms. The estuary tides were wild.")
        .Bookmark("Seasons", 1)
        .Bookmark("Spring", 1, 1)
        .Bookmark("Summer", 2, 1)
        .Bookmark("Autumn", 3, 1);

    [Fact]
    public void Reads_metadata_pages_outline_and_renders_a_cover()
    {
        var book = PdfParser.Parse(SampleBook().WriteTo(_temp.Path, "tides.pdf"));

        Assert.Equal("Field Notes on Tides", book.Metadata.Title);
        Assert.Equal(["Ines Calloway", "Mark Ode"], book.Authors.Select(a => a.Name));
        Assert.Equal("Observations from a year on the coast.", book.Metadata.Description);
        Assert.Equal("2021-03-15", book.Metadata.PublishedDate);
        Assert.Equal(["page/1", "page/2", "page/3", "page/4"], book.Chapters.Select(c => c.Href));
        Assert.Equal(["Seasons", "Spring", "Summer", "Autumn"], book.TableOfContents.Select(t => t.Title));
        Assert.Equal([0, 1, 1, 1], book.TableOfContents.Select(t => t.Depth));
        Assert.Equal([1, 1, 2, 3], book.TableOfContents.Select(t => t.ChapterIndex));

        Assert.Equal("page/1", book.CoverHref);
        Assert.Equal([0xFF, 0xD8], book.CoverImage![..2]);
    }

    [Fact]
    public async Task Provides_page_text_for_search()
    {
        var path = SampleBook().WriteTo(_temp.Path, "search.pdf");
        using var provider = new PdfContentProvider();

        var summer = await provider.GetChapterTextAsync(path, "hash", "page/3", CancellationToken.None);
        Assert.Contains("We measured the neap tides every morning.", summer.Text);
        Assert.DoesNotContain("\n", summer.Text);
        Assert.Equal(string.Empty, (await provider.GetChapterTextAsync(path, "hash", "page/99", CancellationToken.None)).Text);
    }

    [Fact]
    public void Uses_the_file_name_when_the_title_is_missing_or_junk()
    {
        Assert.Equal("my notes", PdfParser.Parse(new PdfBuilder().Title(null).WriteTo(_temp.Path, "my_notes.pdf")).Metadata.Title);
        Assert.Equal("report", PdfParser.Parse(new PdfBuilder().Title("Microsoft Word - report.docx").WriteTo(_temp.Path, "report.pdf")).Metadata.Title);
    }

    [Fact]
    public void Rejects_files_that_are_not_pdfs()
    {
        var path = Path.Combine(_temp.Path, "fake.pdf");
        File.WriteAllText(path, "This is not a PDF at all.");
        Assert.Throws<EbookFormatException>(() => PdfParser.Parse(path));
    }

    [Theory]
    [InlineData("exam-\r\nple of hyphen-\nation", "example of hyphenation")]
    [InlineData("line one\r\nline two", "line one line two")]
    [InlineData("Keep - dashes\r\nAnd Capitals", "Keep - dashes And Capitals")]
    public void Normalizes_line_breaks_for_search(string raw, string expected) =>
        Assert.Equal(expected, PdfContentProvider.Normalize(raw));

    public void Dispose() => _temp.Dispose();
}
