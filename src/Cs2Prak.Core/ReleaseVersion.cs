using System.Text.RegularExpressions;

namespace Cs2Prak.Core;

public static partial class ReleaseVersion
{
    private const int Components = 4;

    public static readonly IComparer<string> Comparer = new TagComparer();

    public static int Compare(string? a, string? b)
    {
        var left = Split(a);
        var right = Split(b);

        for (var i = 0; i < Components; i++)
        {
            var order = left.Numbers[i].CompareTo(right.Numbers[i]);
            if (order != 0) return order;
        }

        if (left.Suffix.Length == 0 && right.Suffix.Length == 0) return 0;
        if (left.Suffix.Length == 0) return 1;
        if (right.Suffix.Length == 0) return -1;

        return string.Compare(left.Suffix, right.Suffix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNewer(string? candidate, string? baseline) =>
        Compare(candidate, baseline) > 0;

    public static string Normalise(string? version)
    {
        var (numbers, suffix) = Split(version);
        var text = $"{numbers[0]}.{numbers[1]}.{numbers[2]:00}";
        if (numbers[3] != 0) text += $".{numbers[3]}";
        return suffix.Length == 0 ? text : $"{text}-{suffix}";
    }

    private static (int[] Numbers, string Suffix) Split(string? version)
    {
        var text = (version ?? "").Trim();
        if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V')) text = text[1..];

        var cut = text.IndexOfAny(['-', '+', ' ']);
        var suffix = cut < 0 ? "" : text[(cut + 1)..].Trim();
        var head = cut < 0 ? text : text[..cut];

        var numbers = new int[Components];
        var index = 0;
        foreach (var match in Numbers().EnumerateMatches(head))
        {
            if (index == Components) break;
            numbers[index++] = int.TryParse(head.AsSpan(match.Index, match.Length), out var value)
                ? value
                : 0;
        }
        return (numbers, suffix);
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Numbers();

    private sealed class TagComparer : IComparer<string>
    {
        public int Compare(string? x, string? y) => ReleaseVersion.Compare(x, y);
    }
}
