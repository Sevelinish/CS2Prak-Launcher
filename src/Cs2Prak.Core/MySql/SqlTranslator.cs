using System.Text.RegularExpressions;

namespace Cs2Prak.Core.MySql;

public static partial class SqlTranslator
{
    [GeneratedRegex(@"^\s*(SET\b|USE\b|CREATE\s+DATABASE|DROP\s+DATABASE|SHOW\b|SELECT\s+@@)",
                    RegexOptions.IgnoreCase)]
    private static partial Regex Skippable();

    [GeneratedRegex(@"^\s*(START\s+TRANSACTION|BEGIN(\s+WORK)?|COMMIT|ROLLBACK|SAVEPOINT|RELEASE\s+SAVEPOINT)\b",
                    RegexOptions.IgnoreCase)]
    private static partial Regex Transactional();

    [GeneratedRegex(@"\bON\s+DUPLICATE\s+KEY\s+UPDATE\b", RegexOptions.IgnoreCase)]
    private static partial Regex OnDuplicateKey();

    [GeneratedRegex(@"\bINSERT\b", RegexOptions.IgnoreCase)]
    private static partial Regex InsertKeyword();

    [GeneratedRegex(@"\s+ON\s+DUPLICATE\s+KEY\s+UPDATE\b[\s\S]*", RegexOptions.IgnoreCase)]
    private static partial Regex OnDuplicateKeyTail();

    [GeneratedRegex(@"^\s*CREATE\s+TABLE", RegexOptions.IgnoreCase)]
    private static partial Regex CreateTable();

    [GeneratedRegex(@"\bCOMMENT\s+'[^']*'", RegexOptions.IgnoreCase)]
    private static partial Regex Comment();

    [GeneratedRegex(@"\bENGINE\s*=\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Engine();

    [GeneratedRegex(@"\bDEFAULT\s+CHARSET\s*=\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultCharset();

    [GeneratedRegex(@"\bCHARSET\s*=\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Charset();

    [GeneratedRegex(@"\bCOLLATE\s*=?\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Collate();

    [GeneratedRegex(@",\s*\)\s*$")]
    private static partial Regex TrailingComma();

    public static string? ToSqlite(string sql)
    {
        var s = sql.Trim();
        if (s.Length == 0) return null;
        if (Skippable().IsMatch(s) || Transactional().IsMatch(s)) return null;

        if (OnDuplicateKey().IsMatch(s))
        {
            s = InsertKeyword().Replace(s, "INSERT OR REPLACE", 1);
            s = OnDuplicateKeyTail().Replace(s, "");
        }

        if (CreateTable().IsMatch(s))
        {
            s = Comment().Replace(s, "");
            s = Engine().Replace(s, "");
            s = DefaultCharset().Replace(s, "");
            s = Charset().Replace(s, "");
            s = Collate().Replace(s, "");
            s = TrailingComma().Replace(s.TrimEnd(';'), "\n)").TrimEnd();
        }

        return s;
    }
}
