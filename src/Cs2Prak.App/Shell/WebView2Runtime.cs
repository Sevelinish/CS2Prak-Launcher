using Cs2Prak.Core;
using Microsoft.Win32;

namespace Cs2Prak.App.Shell;

internal static class WebView2Runtime
{
    private const string RuntimeKey =
        @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    public const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    public static string? Version()
    {
        (RegistryHive Hive, RegistryView View)[] probes =
        [
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.CurrentUser,  RegistryView.Default),
        ];

        foreach (var (hive, view) in probes)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(RuntimeKey);
                if (key?.GetValue("pv") is string pv && pv.Length > 0 && pv != "0.0.0.0")
                    return pv;
            }
            catch (Exception) {  }
        }
        return null;
    }

    public static bool IsInstalled => Version() is not null;

    public static void RecordFailure(Exception e)
    {
        try
        {
            File.WriteAllText(AppPaths.ShellErrorLog,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + e + "\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void ClearFailure()
    {
        try { File.Delete(AppPaths.ShellErrorLog); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static string Summarize(Exception e)
    {
        var chain = new List<Exception>();
        for (Exception? cur = e; cur is not null; cur = cur.InnerException) chain.Add(cur);

        static string Describe(Exception x) => $"{x.GetType().Name}: {x.Message}";

        var symptom = Describe(chain[0]);
        var cause = Describe(chain[^1]);
        var summary = cause == symptom ? cause : cause + "  ->  " + symptom;
        return summary.Length > 300 ? summary[..300] : summary;
    }
}
