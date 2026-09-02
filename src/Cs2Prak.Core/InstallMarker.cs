using System.Text.Json.Nodes;

namespace Cs2Prak.Core;

public static class InstallMarker
{
    private static string Path_ => System.IO.Path.Combine(AppPaths.Root, "release.json");

    public static bool IsInstalled => File.Exists(Path_);

    public static string? ReleaseRepo()
    {
        try
        {
            if (!File.Exists(Path_)) return null;
            var repo = (JsonNode.Parse(File.ReadAllText(Path_)) as JsonObject)?["repo"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(repo) ? null : repo.Trim();
        }
        catch (Exception) { return null; }
    }
}
