using CoffeeReader.Application.Abstractions;
using CoffeeReader.Infrastructure.Ebooks.Epub;
using CoffeeReader.Infrastructure.Ebooks.Html;
using CoffeeReader.Tests.Support;

namespace CoffeeReader.Tests.Ebooks;

public class EpubParserTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public void Reads_epub3_metadata_navigation_series_and_cover()
    {
        var path = new EpubBuilder()
            .Title("The Way of Kings")
            .Author("Brandon Sanderson")
            .Series("The Stormlight Archive", "1")
            .Description("<p>Roshar is a world of <b>stone</b> and storms.</p><p>Second paragraph.</p>")
            .Chapter("Prelude", "<p>Kalak rounded a rocky stone ridge.</p>")
            .Chapter("Stormblessed", "<p>Szeth-son-son-Vallano wore white on the day he was to kill a king.</p>")
            .WriteTo(_temp.Path, "kings.epub");

        var book = EpubParser.Parse(path);

        Assert.Equal("The Way of Kings", book.Metadata.Title);
        Assert.Equal("Brandon Sanderson", Assert.Single(book.Authors).Name);
        Assert.Equal("The Stormlight Archive", book.SeriesName);
        Assert.Equal(1, book.SeriesIndex);
        Assert.Equal("en", book.Metadata.Language);
        Assert.Equal("Test Press", book.Metadata.Publisher);
        Assert.Equal("2001-05-17", book.Metadata.PublishedDate);
        Assert.Equal("Roshar is a world of stone and storms.\n\nSecond paragraph.", book.Metadata.Description);
        Assert.Equal(["OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch2.xhtml"], book.Chapters.Select(c => c.Href));
        Assert.Equal(["Prelude", "Stormblessed"], book.TableOfContents.Select(t => t.Title));
        Assert.Equal([0, 1], book.TableOfContents.Select(t => t.ChapterIndex));
        Assert.Equal("OEBPS/Images/cover.png", book.CoverHref);
        Assert.NotNull(book.CoverImage);
    }

    [Fact]
    public void Reads_epub2_ncx_and_calibre_metadata()
    {
        var path = new EpubBuilder()
            .Epub2()
            .Title("Mistborn")
            .Author("Brandon Sanderson")
            .Series("Mistborn", "2.5")
            .Chapter("Ash", "<p>Ash fell from the sky.</p>")
            .WriteTo(_temp.Path, "mistborn.epub");

        var book = EpubParser.Parse(path);

        Assert.Equal("Mistborn", book.SeriesName);
        Assert.Equal(2.5, book.SeriesIndex);
        Assert.Equal("Sanderson, Brandon", Assert.Single(book.Authors).SortName);
        Assert.Equal("Ash", Assert.Single(book.TableOfContents).Title);
        Assert.Equal("OEBPS/Images/cover.png", book.CoverHref);
    }

    [Fact]
    public void Rejects_files_that_are_not_epubs()
    {
        var path = Path.Combine(_temp.Path, "broken.epub");
        File.WriteAllText(path, "definitely not a zip file");
        Assert.Throws<EbookFormatException>(() => EpubParser.Parse(path));
    }

    public void Dispose() => _temp.Dispose();
}

public class ChapterRenderingTests
{
    [Fact]
    public void Removes_scripts_handlers_and_javascript_links()
    {
        var chapter = HtmlChapterRenderer.Render("""
            <html><head><script>alert(1)</script></head>
            <body><p onclick="steal()">Hello <a href="javascript:alert(1)">there</a></p>
            <script>alert(2)</script><iframe src="https://evil.example"></iframe>
            <img src="../Images/a.png" onerror="alert(3)"/></body></html>
            """, "OEBPS/Text/ch1.xhtml");

        Assert.DoesNotContain("script", chapter.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", chapter.BodyHtml);
        Assert.DoesNotContain("onerror", chapter.BodyHtml);
        Assert.DoesNotContain("javascript:", chapter.BodyHtml);
        Assert.DoesNotContain("iframe", chapter.BodyHtml);
        Assert.Contains("src=\"../Images/a.png\"", chapter.BodyHtml);
        Assert.Contains("Hello", chapter.BodyHtml);
    }

    [Fact]
    public void Expands_xml_self_closing_tags_so_content_isnt_swallowed()
    {
        var chapter = HtmlChapterRenderer.Render(
            "<?xml version=\"1.0\"?><html xmlns=\"http://www.w3.org/1999/xhtml\"><body><p><a id=\"page5\"/>After the anchor</p><p>Next</p></body></html>",
            "ch.xhtml");

        Assert.Contains("<a id=\"page5\"></a>After the anchor", chapter.BodyHtml);
        Assert.Equal("After the anchorNext", HtmlChapterRenderer.PlainText(chapter.BodyHtml));
    }

    [Fact]
    public void Collects_stylesheets_body_attributes_and_keeps_svg_covers()
    {
        var chapter = HtmlChapterRenderer.Render("""
            <html xmlns="http://www.w3.org/1999/xhtml" xml:lang="fr">
            <head><link rel="stylesheet" href="../Styles/main.css"/><style>p { margin: 0 } </style><style></style></head>
            <body class="calibre" id="top">
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 600 800"><image width="600" height="800" xlink:href="../Images/cover.jpg"/></svg>
            </body></html>
            """, "OEBPS/Text/cover.xhtml");

        Assert.Equal(["OEBPS/Styles/main.css"], chapter.Stylesheets);
        Assert.Equal(["p { margin: 0 } "], chapter.InlineStyles);
        Assert.Equal("calibre", chapter.BodyClass);
        Assert.Equal("top", chapter.BodyId);
        Assert.Equal("fr", chapter.Language);
        Assert.Contains("<svg", chapter.BodyHtml);
        Assert.Contains("xlink:href=\"../Images/cover.jpg\"", chapter.BodyHtml);
    }

    [Theory]
    [InlineData("OEBPS/Text", "../Images/a%20b.png", "OEBPS/Images/a b.png")]
    [InlineData("OEBPS/Text", "../../../../etc/passwd", "etc/passwd")]
    [InlineData("", "/OEBPS/./x.css#frag", "OEBPS/x.css")]
    public void Paths_resolve_inside_the_container(string directory, string href, string expected) =>
        Assert.Equal(expected, ContainerPath.Resolve(directory, href));

    [Fact]
    public void Keeps_safe_body_styles_only()
    {
        var chapter = HtmlChapterRenderer.Render(
            "<html><body style=\"margin:0; text-align:center; background:url(javascript:alert(1))\"><img src=\"c.jpg\"/></body></html>",
            "cover.xhtml");
        Assert.Equal("margin:0; text-align:center", chapter.BodyStyle);
    }

    [Fact]
    public void Inline_styles_cannot_close_the_style_element() =>
        Assert.DoesNotContain("</style>", ChapterSanitizer.SanitizeCss("p{} </style><script>alert(1)</script>"));
}
