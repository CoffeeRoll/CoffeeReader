using System.Text.RegularExpressions;
using CoffeeReader.Domain.Common;

namespace CoffeeReader.Infrastructure.Ebooks;

/// <summary>
/// Splits an author field that may name several people, as tools like Calibre write them
/// ("Jane Smith &amp; John Doe" or "Smith, Jane; Doe, John").
/// </summary>
public static partial class AuthorNames
{
    /// <summary>Words that mark a company or imprint rather than people ("John Wiley &amp; Sons, Inc.").</summary>
    private static readonly HashSet<string> CompanyWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "inc", "incorporated", "ltd", "limited", "llc", "llp", "plc", "gmbh", "ag", "co", "corp", "corporation",
        "company", "sons", "brothers", "bros", "associates", "partners", "press", "publishers", "publishing",
        "group", "media", "books", "studio", "studios",
    };

    /// <summary>
    /// Semicolons always separate names. An ampersand only does when every side reads as a person's
    /// full name, so companies and pairs such as "Black &amp; Decker", "Simon &amp; Schuster" or "AT&amp;T"
    /// stay whole.
    /// </summary>
    public static IReadOnlyList<string> Split(string? value)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return names;
        }

        foreach (var item in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var entry = TextNormalization.CollapseWhitespace(item).Trim();
            var parts = Ampersand().Split(entry).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (parts.Count > 1 && !IsCompany(entry) && parts.All(LooksLikeFullName))
            {
                names.AddRange(parts);
            }
            else if (entry.Length > 0)
            {
                names.Add(entry);
            }
        }

        return names;
    }

    /// <summary>"Jane Smith", "J. R. R. Tolkien" or "Smith, Jane"; not a lone word like "Black".</summary>
    private static bool LooksLikeFullName(string part) =>
        part.Contains(',') || part.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2;

    private static bool IsCompany(string value) =>
        Word().Matches(value).Any(word => CompanyWords.Contains(word.Value));

    [GeneratedRegex(@"\s+&\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Ampersand();

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();
}
