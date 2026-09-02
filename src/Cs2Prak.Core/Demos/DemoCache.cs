using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cs2Prak.Core.Demos;

public static partial class DemoCache
{
    private const string CacheVersion = "v16-voice";

    [GeneratedRegex("^[0-9a-f]{1,40}$")]
    private static partial Regex SafeKey();

    public static bool IsSafeKey(string? key) => key is not null && SafeKey().IsMatch(key);

    public static string Key(string path)
    {
        var info = new FileInfo(path);
        var stamp = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
        var raw = $"{CacheVersion}|{info.Name}|{info.Length}|{stamp}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    public static string DataPath(string key) =>
        Path.Combine(AppPaths.DemosCacheDir, key + ".json");

    public static string VoiceDir(string key) =>
        Path.Combine(AppPaths.DemosCacheDir, key + "_voice");

    public static bool IsCached(string key) => IsSafeKey(key) && File.Exists(DataPath(key));

    public static void Forget(string key)
    {
        if (!IsSafeKey(key)) return;
        try { File.Delete(DataPath(key)); } catch (Exception) { }
        try { if (Directory.Exists(VoiceDir(key))) Directory.Delete(VoiceDir(key), recursive: true); }
        catch (Exception) { }
    }
}
