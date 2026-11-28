using System.Text;
using CoffeeReader.Application.Abstractions;
using CoffeeReader.Infrastructure.Ebooks.Mobi;
using CoffeeReader.Tests.Support;

namespace CoffeeReader.Tests.Ebooks;

public class MobiTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private static MobiBuilder SampleBook() => new MobiBuilder()
        .Title("The Lighthouse Keeper")
        .Author("Ada Fenwick")
        .Description("<p>A <b>quiet</b> book about light.</p>")
        .Chapter("Arrival", "<p>The boat came in on the morning tide.{{image}}</p><p>See the {{link:2|epilogue}}.</p>")
        .Chapter("The Lamp", "<p>Every night the lamp turned, patient as a clock.</p>")
        .Chapter("Epilogue", "<p>Years later the light still burned.</p>");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reads_metadata_chapters_toc_and_cover(bool compressed)
    {
        var builder = SampleBook();
        if (!compressed)
        {
            builder.Uncompressed();
        }

        var book = MobiParser.Parse(builder.WriteTo(_temp.Path, $"lighthouse-{compressed}.mobi"));

        Assert.Equal("The Lighthouse Keeper", book.Metadata.Title);
        Assert.Equal("Ada Fenwick", Assert.Single(book.Authors).Name);
        Assert.Equal("A quiet book about light.", book.Metadata.Description);
        Assert.Equal("Test Press", book.Metadata.Publisher);
        Assert.Equal("9780000000002", book.Metadata.Isbn);
        Assert.Equal("2019-04-02", book.Metadata.PublishedDate);
        Assert.Equal("en", book.Metadata.Language);

        // Contents page + three chapters, split at page breaks.
        Assert.Equal(4, book.Chapters.Count);
        Assert.Equal(["Arrival", "The Lamp", "Epilogue"], book.TableOfContents.Select(t => t.Title));
        Assert.Equal([1, 2, 3], book.TableOfContents.Select(t => t.ChapterIndex));
        Assert.All(book.TableOfContents, t => Assert.StartsWith("filepos", t.Fragment));

        Assert.Equal("images/00001", book.CoverHref);
        Assert.Equal(0x89, book.CoverImage![0]);
    }

    [Fact]
    public async Task Renders_chapters_with_working_links_and_images()
    {
        var path = SampleBook().WriteTo(_temp.Path, "render.mobi");
        using var provider = new MobiContentProvider();

        var arrival = await provider.GetChapterAsync(path, "hash", "text/part0002.html", CancellationToken.None);
        Assert.Contains("The boat came in on the morning tide.", arrival.BodyHtml);
        Assert.Contains("src=\"../images/00001\"", arrival.BodyHtml);
        Assert.Matches("href=\"part0004.html#filepos\\d+\"", arrival.BodyHtml);
        Assert.DoesNotContain("mbp:pagebreak", arrival.BodyHtml);

        var epilogue = await provider.GetChapterAsync(path, "hash", "text/part0004.html", CancellationToken.None);
        Assert.Matches("id=\"filepos\\d+\"", epilogue.BodyHtml);

        var image = await provider.OpenResourceAsync(path, "images/00001", CancellationToken.None);
        Assert.Equal("image/png", image!.ContentType);
        Assert.Null(await provider.OpenResourceAsync(path, "images/00099", CancellationToken.None));
        Assert.Null(await provider.OpenResourceAsync(path, "../secret", CancellationToken.None));

        var text = await provider.GetChapterTextAsync(path, "hash", "text/part0003.html", CancellationToken.None);
        Assert.Contains("patient as a clock", text.Text);
    }

    [Fact]
    public async Task Decodes_windows_1252_text()
    {
        var path = new MobiBuilder().Windows1252().WithoutToc()
            .Chapter("Café", "<p>It’s “quoted” — naïve.</p>")
            .WriteTo(_temp.Path, "cp1252.mobi");

        using var provider = new MobiContentProvider();
        var chapter = await provider.GetChapterAsync(path, "cp", "text/part0001.html", CancellationToken.None);
        Assert.Contains("It’s “quoted” — naïve.", chapter.BodyHtml);
        Assert.Equal("Café", Assert.Single(MobiParser.Parse(path).TableOfContents).Title);
    }

    [Fact]
    public void Rejects_drm_kf8_and_non_mobi_files()
    {
        var drm = Assert.Throws<EbookFormatException>(() => MobiParser.Parse(new MobiBuilder().Encrypted().WriteTo(_temp.Path, "drm.mobi")));
        Assert.Contains("DRM", drm.Message);

        var kf8 = Assert.Throws<EbookFormatException>(() => MobiParser.Parse(new MobiBuilder().Kf8().WriteTo(_temp.Path, "kf8.azw3")));
        Assert.Contains("AZW3", kf8.Message);

        var junk = Path.Combine(_temp.Path, "junk.mobi");
        File.WriteAllBytes(junk, Encoding.ASCII.GetBytes(new string('x', 200)));
        Assert.Throws<EbookFormatException>(() => MobiParser.Parse(junk));
    }

    [Fact]
    public void PalmDoc_round_trips()
    {
        var text = Encoding.UTF8.GetBytes(string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog — ünïcode \u0001 bytes.", 40)));
        var output = new List<byte>();
        MobiDecompression.PalmDoc(MobiBuilder.CompressPalmDoc(text), output);
        Assert.Equal(text, output.ToArray());
    }

    public void Dispose() => _temp.Dispose();
}
