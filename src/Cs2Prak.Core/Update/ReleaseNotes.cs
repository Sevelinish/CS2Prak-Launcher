using System.Text;
using System.Text.RegularExpressions;

namespace Cs2Prak.Core.Update;

public static partial class ReleaseNotes
{
    public static string Plain(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";

        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var kept = new List<string>(lines.Length);
        var inFence = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal)
                || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
            {
                kept.Add(line);
                continue;
            }

            if (IsRule(trimmed)) continue;
            if (IsTableDivider(trimmed)) continue;

            kept.Add(Line(line));
        }

        return Tidy(kept);
    }

    private static string Line(string line)
    {
        var indent = line.Length - line.TrimStart().Length;
        var text = line.TrimStart();

        while (text.StartsWith('>')) text = text[1..].TrimStart();

        text = Heading().Replace(text, "");

        var bullet = Bullet().Match(text);
        if (bullet.Success)
        {
            var marker = bullet.Groups[1].Value;
            var numbered = char.IsAsciiDigit(marker[0]);
            text = (numbered ? marker + " " : "- ") + text[bullet.Length..];
        }

        text = Image().Replace(text, "");
        text = Link().Replace(text, "$1");
        text = InlineCode().Replace(text, "$1");
        text = Emphasis().Replace(text, "$2");
        text = Strikethrough().Replace(text, "$1");
        text = text.Replace("|", " ").Replace("&nbsp;", " ");

        return new string(' ', Math.Min(indent, 4)) + text.TrimEnd();
    }

    private static string Tidy(List<string> lines)
    {
        var text = new StringBuilder();
        var blanks = 0;

        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                blanks++;
                continue;
            }
            if (text.Length > 0) text.Append('\n', blanks > 0 ? 2 : 1);
            blanks = 0;
            text.Append(line);
        }
        return text.ToString();
    }

    private static bool IsRule(string line) =>
        line.Length >= 3
        && (line.All(c => c == '*') || line.All(c => c == '-') || line.All(c => c == '_'));

    private static bool IsTableDivider(string line) =>
        line.Length >= 3
        && line.Contains('-')
        && line.All(c => c is '-' or '|' or ':' or ' ');

    [GeneratedRegex(@"^#{1,6}\s*")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^([-*+]|\d+\.)\s+")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"`([^`]*)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"(\*\*\*|\*\*|\*|___|__|_)(.+?)\1")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"~~(.+?)~~")]
    private static partial Regex Strikethrough();
}
