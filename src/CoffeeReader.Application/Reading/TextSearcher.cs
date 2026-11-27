using System.Globalization;
using System.Text;

namespace CoffeeReader.Application.Reading;

/// <summary>
/// Case-, accent- and whitespace-insensitive text matching.
/// </summary>
/// <remarks>
/// The web reader implements the same folding rules (web/src/features/reader/textSearch.ts) so that
/// "the Nth match in chapter X" found here points at the same text the browser highlights.
/// </remarks>
public static class TextSearcher
{
    public readonly record struct Match(int Start, int Length);

    public sealed record Snippet(string Before, string Match, string After);

    /// <summary>
    /// Folds text and records, for every folded UTF-16 unit, the index of the source character it came from.
    /// </summary>
    public static (string Folded, int[] Map) Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        var lastWasSpace = true;
        Span<char> buffer = stackalloc char[2];
        var index = 0;
        while (index < text.Length)
        {
            Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);
            if (IsSpace(rune.Value))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    map.Add(index);
                    lastWasSpace = true;
                }
            }
            else
            {
                foreach (var part in rune.ToString().Normalize(NormalizationForm.FormD).EnumerateRunes())
                {
                    if (Rune.GetUnicodeCategory(part) == UnicodeCategory.NonSpacingMark)
                    {
                        continue;
                    }

                    var lower = Rune.ToLowerInvariant(part);
                    var written = lower.EncodeToUtf16(buffer);
                    for (var i = 0; i < written; i++)
                    {
                        builder.Append(buffer[i]);
                        map.Add(index);
                    }

                    lastWasSpace = false;
                }
            }

            index += consumed;
        }

        return (builder.ToString(), map.ToArray());
    }

    public static string FoldQuery(string query) => Fold(query).Folded.Trim();

    /// <summary>Finds non-overlapping matches, in order, as ranges of the original text.</summary>
    public static IEnumerable<Match> FindAll(string text, string query)
    {
        var needle = FoldQuery(query);
        if (needle.Length == 0 || text.Length == 0)
        {
            yield break;
        }

        var (haystack, map) = Fold(text);
        var position = 0;
        while (position <= haystack.Length - needle.Length)
        {
            var found = haystack.IndexOf(needle, position, StringComparison.Ordinal);
            if (found < 0)
            {
                yield break;
            }

            var start = map[found];
            var lastSource = map[found + needle.Length - 1];
            var end = lastSource + (char.IsHighSurrogate(text[lastSource]) && lastSource + 1 < text.Length ? 2 : 1);
            yield return new Match(start, end - start);
            position = found + needle.Length;
        }
    }

    public static Snippet CreateSnippet(string text, Match match, IReadOnlyList<int>? blockBreaks = null, int context = 70)
    {
        var beforeStart = Math.Max(0, match.Start - context);
        var afterEnd = Math.Min(text.Length, match.Start + match.Length + context);
        if (blockBreaks is { Count: > 0 })
        {
            // Show paragraph and heading boundaries as spaces ("coin. Nobody", not "coin.Nobody").
            var breaks = blockBreaks.Where(b => b > beforeStart && b < afterEnd).ToHashSet();
            string Slice(int from, int to)
            {
                var builder = new StringBuilder(to - from + 8);
                for (var i = from; i < to; i++)
                {
                    if (breaks.Contains(i))
                    {
                        builder.Append(' ');
                    }

                    builder.Append(text[i]);
                }

                return builder.ToString();
            }

            return Finish(text, Slice(beforeStart, match.Start), beforeStart, Slice(match.Start, match.Start + match.Length), Slice(match.Start + match.Length, afterEnd), afterEnd);
        }

        return Finish(text, text[beforeStart..match.Start], beforeStart, text.Substring(match.Start, match.Length), text[(match.Start + match.Length)..afterEnd], afterEnd);
    }

    private static Snippet Finish(string text, string before, int beforeStart, string matched, string after, int afterEnd)
    {
        if (beforeStart > 0)
        {
            var space = before.IndexOf(' ');
            before = "…" + (space >= 0 && space < before.Length - 1 ? before[(space + 1)..] : before);
        }

        if (afterEnd < text.Length)
        {
            var space = after.LastIndexOf(' ');
            after = (space > 0 ? after[..space] : after) + "…";
        }

        return new Snippet(
            Collapse(before).TrimStart(),
            Collapse(matched),
            Collapse(after).TrimEnd());
    }

    /// <summary>The whitespace set shared with the web reader.</summary>
    public static bool IsSpace(int codePoint) => codePoint switch
    {
        0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x20 or 0x85 or 0xA0 or 0x1680 => true,
        >= 0x2000 and <= 0x200A => true,
        0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 or 0xFEFF => true,
        _ => false,
    };

    private static string Collapse(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (IsSpace(rune.Value))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
            }
            else
            {
                builder.Append(rune.ToString());
                lastWasSpace = false;
            }
        }

        return builder.ToString();
    }
}
