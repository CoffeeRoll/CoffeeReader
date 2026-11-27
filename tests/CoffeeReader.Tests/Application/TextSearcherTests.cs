using CoffeeReader.Application.Reading;

namespace CoffeeReader.Tests.Application;

public class TextSearcherTests
{
    [Fact]
    public void Finds_matches_ignoring_case_accents_and_whitespace()
    {
        const string text = "The Café was quiet.\n   Later, the   cafe OWNER returned to the CAFÉ.";
        var matches = TextSearcher.FindAll(text, "cafe").ToList();

        Assert.Equal(3, matches.Count);
        Assert.Equal("Café", text.Substring(matches[0].Start, matches[0].Length));
        Assert.Equal("CAFÉ", text.Substring(matches[2].Start, matches[2].Length));

        var phrase = TextSearcher.FindAll(text, "cafe owner").Single();
        Assert.Equal("cafe OWNER", text.Substring(phrase.Start, phrase.Length));
    }

    [Fact]
    public void Snippets_show_surrounding_words()
    {
        var text = string.Join(' ', Enumerable.Repeat("filler", 30)) + " the needle is here " + string.Join(' ', Enumerable.Repeat("words", 30));
        var match = TextSearcher.FindAll(text, "needle").Single();
        var snippet = TextSearcher.CreateSnippet(text, match);

        Assert.Equal("needle", snippet.Match);
        Assert.StartsWith("…", snippet.Before);
        Assert.EndsWith("the ", snippet.Before);
        Assert.EndsWith("…", snippet.After);
    }

    [Fact]
    public void Empty_queries_match_nothing() =>
        Assert.Empty(TextSearcher.FindAll("anything", "   "));
}
