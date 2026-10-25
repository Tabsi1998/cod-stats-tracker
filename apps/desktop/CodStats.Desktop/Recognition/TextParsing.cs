using System.Globalization;
using System.Text;
using CodStats.Contracts;

namespace CodStats.Desktop.Recognition;

/// <summary>Turns OCR text into values. Anything it is not sure about becomes null, never a guess.</summary>
public static class TextParsing
{
    // Letters OCR typically reads instead of digits in game fonts.
    private static readonly Dictionary<char, char> DigitLookalikes = new()
    {
        ['O'] = '0',
        ['o'] = '0',
        ['D'] = '0',
        ['Q'] = '0',
        ['I'] = '1',
        ['l'] = '1',
        ['|'] = '1',
        ['i'] = '1',
        ['Z'] = '2',
        ['S'] = '5',
        ['s'] = '5',
        ['B'] = '8',
    };

    /// <summary>A whole number, with how much it had to be repaired (1 = clean, lower = lookalikes replaced).</summary>
    public static (int? Value, double Quality) Count(string? text)
    {
        var trimmed = (text ?? "").Trim().Replace(" ", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal);
        if (trimmed.Length is 0 or > 7)
        {
            return (null, 0);
        }
        var digits = new StringBuilder(trimmed.Length);
        var repaired = 0;
        foreach (var c in trimmed)
        {
            if (char.IsAsciiDigit(c))
            {
                digits.Append(c);
            }
            else if (DigitLookalikes.TryGetValue(c, out var digit))
            {
                digits.Append(digit);
                repaired++;
            }
            else
            {
                return (null, 0);
            }
        }
        // More repaired than read: that was not a number.
        if (repaired * 2 > digits.Length)
        {
            return (null, 0);
        }
        return (int.Parse(digits.ToString(), CultureInfo.InvariantCulture), repaired == 0 ? 1.0 : 0.8);
    }

    /// <summary>
    /// The numbers in a text such as "XP GESAMT 12410" (count 1: the first) or "300 | 0" (count 2: the last two,
    /// since an emblem left of a score line can read as a number). The bar between two scores is often read as "1"
    /// ("300 1 0"), so a lone 1 between other numbers is dropped. Null when there are not enough numbers.
    /// </summary>
    public static IReadOnlyList<int>? Numbers(string? text, int count)
    {
        var numbers = System.Text.RegularExpressions.Regex.Split(text ?? "", @"[^\p{L}\p{N}]+")
            .Select(token => Count(token).Value)
            .OfType<int>()
            .ToList();
        for (var i = numbers.Count - 2; i >= 1 && numbers.Count > count; i--)
        {
            if (numbers[i] == 1)
            {
                numbers.RemoveAt(i);
            }
        }
        if (numbers.Count < count)
        {
            return null;
        }
        return count == 1 ? [numbers[0]] : numbers[^count..];
    }

    /// <summary>Placement texts like "#3", "3rd", "3." or "PLATZ 3".</summary>
    public static (int? Value, double Quality) Placement(string? text)
    {
        var digits = new string((text ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is 0 or > 3)
        {
            return (null, 0);
        }
        return (int.Parse(digits, CultureInfo.InvariantCulture), 0.95);
    }

    /// <summary>The result from the words a profile lists for win, loss and draw.</summary>
    public static MatchResult? Result(string? text, IReadOnlyList<string> win, IReadOnlyList<string> loss, IReadOnlyList<string> draw)
    {
        var normal = Normalise(text);
        if (normal.Length == 0)
        {
            return null;
        }
        var hits = new List<MatchResult>();
        if (win.Any(w => normal.Contains(Normalise(w), StringComparison.Ordinal))) hits.Add(MatchResult.Win);
        if (loss.Any(w => normal.Contains(Normalise(w), StringComparison.Ordinal))) hits.Add(MatchResult.Loss);
        if (draw.Any(w => normal.Contains(Normalise(w), StringComparison.Ordinal))) hits.Add(MatchResult.Draw);
        // Two different results in one text means the region shows something else.
        return hits.Count == 1 ? hits[0] : null;
    }

    /// <summary>Upper case, letters and digits only, so "Victory!" and "VICTORY" compare equal.</summary>
    public static string Normalise(string? text) =>
        new((text ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// The one name of the list the text is, comparing look-alike characters as one (9/5/3 and S, 0/D/Q and O, 1/L
    /// and I, 8 and B, 2 and Z, 6 and G) and ignoring spaces and punctuation; null when it fits none or several.
    /// No other spelling error is allowed: a new weapon close to a known one must not count as the known one.
    /// </summary>
    public static string? OneOf(string? text, IReadOnlyList<string> names)
    {
        var read = LookAlike(text);
        if (read.Length == 0)
        {
            return null;
        }
        var hits = names.Where(n => LookAlike(n) == read).Distinct().ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    /// <summary>The text with look-alike characters made one (see <see cref="OneOf"/>).</summary>
    public static string LookAlike(string? text) =>
        new(Normalise(text).Select(c => c switch
        {
            '9' or '5' or '3' => 'S',
            '0' or 'D' or 'Q' => 'O',
            '1' or 'L' => 'I',
            '8' => 'B',
            '2' => 'Z',
            '6' => 'G',
            'Ä' => 'A',
            'Ö' => 'O',
            'Ü' => 'U',
            _ => c,
        }).ToArray());

    /// <summary>The first of the words the text contains (see <see cref="ContainsAny"/>), as written in the list.</summary>
    public static string? FirstOf(string? text, IEnumerable<string> words) =>
        words.FirstOrDefault(word => ContainsAny(text, [word]));

    /// <summary>True when OCR text contains one of the words, allowing one wrong letter in longer words.</summary>
    public static bool ContainsAny(string? text, IEnumerable<string> words)
    {
        var normal = Normalise(text);
        foreach (var word in words.Select(Normalise).Where(w => w.Length > 0))
        {
            if (normal.Contains(word, StringComparison.Ordinal))
            {
                return true;
            }
            if (word.Length >= 6)
            {
                for (var start = 0; start + word.Length <= normal.Length; start++)
                {
                    var mismatches = 0;
                    for (var i = 0; i < word.Length && mismatches <= 1; i++)
                    {
                        if (normal[start + i] != word[i])
                        {
                            mismatches++;
                        }
                    }
                    if (mismatches <= 1)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }
}
