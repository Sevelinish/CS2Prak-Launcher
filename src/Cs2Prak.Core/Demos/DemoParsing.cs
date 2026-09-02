using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Demos;

public sealed record ParsedDemo(string Key, JsonObject Meta);

public static class DemoParsing
{
    public static Func<string, ParsedDemo>? Parse;

    public static bool Available => Parse is not null;

    public const string Unavailable =
        "Demo parsing is not available in this build yet.";

    public static ParsedDemo Run(string path)
    {
        var parse = Parse ?? throw new InvalidOperationException(Unavailable);

        var process = System.Diagnostics.Process.GetCurrentProcess();
        var previous = process.PriorityClass;
        try
        {
            process.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
            return parse(path);
        }
        finally
        {
            try { process.PriorityClass = previous; } catch (Exception) { }
        }
    }
}
