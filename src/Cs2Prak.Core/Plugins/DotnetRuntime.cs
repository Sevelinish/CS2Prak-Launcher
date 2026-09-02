using System.Diagnostics;

namespace Cs2Prak.Core.Plugins;

public static class DotnetRuntime
{
    public static void Ensure(JobLog log)
    {
        if (File.Exists(Path.Combine(AppPaths.CssBase, "dotnet", "dotnet.exe")))
        {
            log.Add("[+] CSS with-runtime: .NET 8 bundled, no system install needed.");
            return;
        }

        if (SystemHasNet8())
        {
            log.Add("[+] .NET 8 runtime found on system.");
            return;
        }

        log.Add(".NET 8 runtime not found — downloading (CounterStrikeSharp requires it)...");
        try
        {
            Install(log);
        }
        catch (Exception e)
        {
            log.Add($"WARNING: Could not auto-install .NET 8: {e.Message}");
            log.Add("  → Re-download CSS using the \"with-runtime\" zip from GitHub.");
        }
    }

    private static bool SystemHasNet8()
    {
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
        var dotnet = Path.Combine(programFiles, "dotnet", "dotnet.exe");
        if (!File.Exists(dotnet)) return false;

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = dotnet,
                Arguments = "--list-runtimes",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            if (proc is null) return false;

            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(8000)) return false;
            return output.Contains("Microsoft.NETCore.App 8.", StringComparison.Ordinal);
        }
        catch (Exception) { return false; }
    }

    private static void Install(JobLog log)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("cs2prak/1.0");

        var version = http.GetStringAsync(AppPaths.Dotnet8VerUrl).GetAwaiter().GetResult().Trim();
        var url = $"https://dotnetcli.azureedge.net/dotnet/Runtime/{version}"
                  + $"/dotnet-runtime-{version}-win-x64.exe";

        log.Add($"Downloading .NET Runtime {version}...");
        var installer = Path.Combine(Path.GetTempPath(), $"dotnet-runtime-{version}-win-x64.exe");

        using (var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                                  .GetAwaiter().GetResult())
        {
            response.EnsureSuccessStatusCode();
            using var stream = response.Content.ReadAsStream();
            using var file = File.Create(installer);
            stream.CopyTo(file);
        }

        log.Add("Running .NET 8 installer silently...");
        int code;
        using (var proc = Process.Start(new ProcessStartInfo
               {
                   FileName = installer,
                   Arguments = "/install /quiet /norestart",
                   UseShellExecute = false,
                   CreateNoWindow = true,
               }))
        {
            if (proc is null) throw new InvalidOperationException("Could not start the .NET installer.");
            proc.WaitForExit();
            code = proc.ExitCode;
        }

        try { File.Delete(installer); } catch (Exception) { }

        if (code is 0 or 3010)
        {
            log.Add("[+] .NET 8 runtime installed successfully.");
        }
        else
        {
            log.Add($"WARNING: .NET installer returned code {code}.");
            log.Add("  → If CSS still fails, re-download CSS using the \"with-runtime\" zip.");
        }
    }
}
