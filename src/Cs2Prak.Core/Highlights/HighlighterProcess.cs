using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Cs2Prak.Core.Highlights;

public sealed record HighlighterEndpoint(string BaseUrl, string Token);

public static class HighlighterProcess
{
    private static readonly Lock Gate = new();
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    private static Process? _process;
    private static HighlighterEndpoint? _endpoint;

    public static string LastError { get; private set; } = "";

    public static bool IsRunning
    {
        get
        {
            lock (Gate) return _process is { HasExited: false } && _endpoint is not null;
        }
    }

    public static HighlighterEndpoint? Current
    {
        get { lock (Gate) return IsRunningLocked() ? _endpoint : null; }
    }

    private static bool IsRunningLocked() => _process is { HasExited: false } && _endpoint is not null;

    public static HighlighterEndpoint Ensure()
    {
        lock (Gate)
        {
            if (IsRunningLocked()) return _endpoint!;

            StopLocked();

            var exe = HighlighterInstall.Exe()
                      ?? throw new InvalidOperationException(
                          "HighlighterCS2 is not installed yet. Install it from the Highlights tab.");

            var home = Path.GetDirectoryName(exe)!;
            var work = Path.Combine(home, "work");
            AppPaths.EnsureDir(work);

            var endpointFile = Path.Combine(work, "api.json");
            try { File.Delete(endpointFile); } catch (Exception) { }

            var start = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = home,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("--api");
            start.ArgumentList.Add("http");
            start.ArgumentList.Add("--api-port");
            start.ArgumentList.Add("0");
            start.ArgumentList.Add("--api-endpoint-file");
            start.ArgumentList.Add(endpointFile);

            var process = Process.Start(start)
                          ?? throw new InvalidOperationException("Could not start HighlighterCS2.exe.");

            Drain(process.StandardOutput);
            Drain(process.StandardError);

            _process = process;
            _endpoint = WaitForEndpoint(process, endpointFile);
            return _endpoint;
        }
    }

    private static HighlighterEndpoint WaitForEndpoint(Process process, string endpointFile)
    {
        var deadline = DateTime.UtcNow + StartTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                LastError = $"HighlighterCS2 exited with code {process.ExitCode} before it served the API.";
                throw new InvalidOperationException(LastError);
            }

            if (Read(endpointFile) is { } endpoint) return endpoint;
            Thread.Sleep(250);
        }

        LastError = "HighlighterCS2 did not report its API endpoint in time.";
        try { process.Kill(entireProcessTree: true); } catch (Exception) { }
        throw new InvalidOperationException(LastError);
    }

    private static HighlighterEndpoint? Read(string endpointFile)
    {
        try
        {
            if (!File.Exists(endpointFile)) return null;

            using var stream = new FileStream(endpointFile, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            if (JsonNode.Parse(reader.ReadToEnd()) is not JsonObject root) return null;

            var baseUrl = root["baseUrl"]?.GetValue<string>();
            var token = root["token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token)) return null;

            return new HighlighterEndpoint(baseUrl.TrimEnd('/'), token);
        }
        catch (Exception) { return null; }
    }

    private static void Drain(StreamReader reader)
    {
        new Thread(() =>
        {
            try { while (reader.ReadLine() is not null) { } }
            catch (Exception) { }
        })
        {
            IsBackground = true,
            Name = "highlighter-drain",
        }.Start();
    }

    public static void Stop()
    {
        lock (Gate) StopLocked();
    }

    private static void StopLocked()
    {
        var process = _process;
        _process = null;
        _endpoint = null;

        if (process is null) return;

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception) { }
        finally
        {
            try { process.Dispose(); } catch (Exception) { }
        }
    }
}
