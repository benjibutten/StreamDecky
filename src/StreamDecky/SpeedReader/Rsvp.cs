using System.Globalization;
using System.Text.RegularExpressions;

namespace StreamDecky.SpeedReader;

/// <summary>
/// One word as shown in the reader, split around the letter the eye should fixate on.
/// </summary>
/// <param name="DelayFactor">Multiplier on the base display time for this word.</param>
public sealed record RsvpWord(string Text, int PivotIndex, double DelayFactor)
{
    public string Before => Text[..PivotIndex];
    public string Pivot => Text.Substring(PivotIndex, PivotLength);
    public string After => Text[(PivotIndex + PivotLength)..];

    /// <summary>Character count of the longer side around the pivot, for sizing the word to fit.</summary>
    public int LongestSide => Math.Max(PivotIndex, Text.Length - PivotIndex - PivotLength);

    private int PivotLength => StringInfo.GetNextTextElementLength(Text, PivotIndex);
}

public static partial class Rsvp
{
    private const int MaxWordLength = 13;
    private const int TargetPartLength = 10;

    /// <summary>
    /// Splits <paramref name="text"/> into words for rapid serial visual presentation.
    /// </summary>
    public static List<RsvpWord> Parse(string text)
    {
        // Composed form keeps letters like å and é in one char, so the pivot never splits off an accent.
        text = text.Normalize();
        var matches = WordPattern().Matches(text);
        var words = new List<RsvpWord>(matches.Count);

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var gapStart = match.Index + match.Length;
            var gapEnd = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var endsParagraph = text.AsSpan(gapStart, gapEnd - gapStart).Count('\n') >= 2;

            var parts = SplitLongWord(match.Value);
            for (var p = 0; p < parts.Count; p++)
            {
                var isLast = p == parts.Count - 1;
                words.Add(new RsvpWord(parts[p], PivotIndex(parts[p]), DelayFactor(parts[p], isLast && endsParagraph)));
            }
        }

        return words;
    }

    // Long words are shown as hyphenated parts so they stay large enough to take in at a glance.
    private static List<string> SplitLongWord(string word)
    {
        var parts = new List<string>();

        foreach (var piece in HyphenPattern().Split(word))
        {
            if (piece.Length <= MaxWordLength)
            {
                parts.Add(piece);
                continue;
            }

            var count = (piece.Length + TargetPartLength - 1) / TargetPartLength;
            var size = (piece.Length + count - 1) / count;
            for (var start = 0; start < piece.Length;)
            {
                var length = Math.Min(size, piece.Length - start);
                if (char.IsHighSurrogate(piece[start + length - 1]) && start + length < piece.Length)
                    length++;

                var isLast = start + length == piece.Length;
                parts.Add(isLast ? piece.Substring(start, length) : piece.Substring(start, length) + "-");
                start += length;
            }
        }

        return parts;
    }

    // The optimal recognition point sits slightly left of the word's centre.
    private static int PivotIndex(string word)
    {
        var start = 0;
        while (start < word.Length && !char.IsLetterOrDigit(word[start]))
            start++;

        if (start == word.Length)
            return 0;

        var end = word.Length - 1;
        while (end > start && !char.IsLetterOrDigit(word[end]))
            end--;

        var offset = (end - start + 1) switch
        {
            <= 1 => 0,
            <= 5 => 1,
            <= 9 => 2,
            <= 13 => 3,
            _ => 4,
        };
        return start + offset;
    }

    // Pauses at punctuation and on long words give the reader time to absorb what was just read.
    private static double DelayFactor(string word, bool endsParagraph)
    {
        if (endsParagraph)
            return 3.0;

        var trimmed = word.TrimEnd('"', '\'', ')', ']', '”', '’', '»');
        var last = trimmed.Length > 0 ? trimmed[^1] : word[^1];

        var factor = last switch
        {
            '.' or '!' or '?' or '…' => 2.2,
            ',' or ';' or ':' or '–' or '—' => 1.5,
            _ => 1.0,
        };

        if (word.Length > 8)
            factor += 0.3;

        return factor;
    }

    [GeneratedRegex(@"\S+")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"(?<=-)(?=\w)")]
    private static partial Regex HyphenPattern();
}
