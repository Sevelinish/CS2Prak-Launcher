using System.Diagnostics;
using System.IO.Compression;

namespace Cs2Prak.Core;

public static class SteamCmd
{
    public static int InstallServer(JobLog log)
    {
        AppPaths.EnsureDir(AppPaths.ServerRoot);

        if (!File.Exists(AppPaths.SteamCmd))
        {
            log.Add("Downloading SteamCMD...");
            Download(AppPaths.SteamCmdUrl, Path.Combine(AppPaths.ServerRoot, "steamcmd.zip"));

            var zip = Path.Combine(AppPaths.ServerRoot, "steamcmd.zip");
            ZipFile.ExtractToDirectory(zip, AppPaths.ServerRoot, overwriteFiles: true);
            File.Delete(zip);
            log.Add("SteamCMD ready.");
        }
        else
        {
            log.Add("SteamCMD already present.");
        }

        log.Add("SteamCMD window is now open — watch it for download progress.");
        log.Add("This may take 10–30 minutes depending on your internet speed.");

        var code = RunAppUpdate();
        if (code != 0) throw new InvalidOperationException($"SteamCMD exited with code {code}");

        Overlay.PatchGameinfo();
        log.Add("Done! CS2 server is installed.");
        return 0;
    }

    public static int UpdateServer(JobLog log)
    {
        log.Add("SteamCMD window is now open — watch it for update progress.");

        var code = RunAppUpdate();
        if (code == 0)
        {
            Overlay.PatchGameinfo();
            log.Add("[cs2prak] gameinfo.gi patched for Metamod.");
            log.Add("Server updated successfully.");
        }
        else
        {
            log.Add($"SteamCMD exited with code {code}");
        }
        return code;
    }

    private static int RunAppUpdate()
    {
        var psi = new ProcessStartInfo
        {
            FileName = AppPaths.SteamCmd,
            WorkingDirectory = AppPaths.SteamCmdDir,
            UseShellExecute = true,
        };
        foreach (var arg in new[] { "+login", "anonymous", "+app_update", "730", "validate", "+quit" })
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Could not start SteamCMD.");
        proc.WaitForExit();
        return proc.ExitCode;
    }

    private static void Download(string url, string dest)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("cs2prak/1.0");

        using var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                                 .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        using var stream = response.Content.ReadAsStream();
        using var file = File.Create(dest);
        stream.CopyTo(file);
    }
}
