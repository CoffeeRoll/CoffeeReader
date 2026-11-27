using System.Globalization;
using System.Text;

namespace CoffeeReader.Domain.Common;

public static class TextNormalization
{
    private static readonly string[] LeadingArticles = ["the ", "a ", "an "];

    /// <summary>
    /// Produces a case- and accent-insensitive key used for uniqueness checks
    /// (usernames, author names, series names).
    /// </summary>
    public static string NormalizeKey(string value)
    {
        var collapsed = CollapseWhitespace(value).Trim();
        var decomposed = collapsed.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Builds a title suitable for alphabetical sorting ("The Hobbit" sorts as "Hobbit, The").
    /// </summary>
    public static string ToSortTitle(string title)
    {
        var trimmed = CollapseWhitespace(title).Trim();
        foreach (var article in LeadingArticles)
        {
            if (trimmed.Length > article.Length && trimmed.StartsWith(article, StringComparison.OrdinalIgnoreCase))
            {
                return $"{trimmed[article.Length..]}, {trimmed[..(article.Length - 1)]}";
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Builds a "Last, First" style sort name for an author when the book doesn't provide one.
    /// </summary>
    public static string ToAuthorSortName(string name)
    {
        var trimmed = CollapseWhitespace(name).Trim();
        if (trimmed.Contains(','))
        {
            return trimmed;
        }

        var lastSpace = trimmed.LastIndexOf(' ');
        return lastSpace <= 0 ? trimmed : $"{trimmed[(lastSpace + 1)..]}, {trimmed[..lastSpace]}";
    }

    public static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
            }
            else
            {
                builder.Append(c);
                previousWasSpace = false;
            }
        }

        return builder.ToString();
    }
}
