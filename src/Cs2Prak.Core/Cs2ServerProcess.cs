using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Cs2Prak.Core;

public static partial class Cs2ServerProcess
{
    [GeneratedRegex("^[A-Za-z0-9_]{1,64}$")]
    private static partial Regex MapName();

    private static readonly object Gate = new();
    private static SafeProcess? _proc;

    public static IntPtr ConsoleHwnd;

    public static Action? OnStopped;

    public static bool IsRunning
    {
        get { lock (Gate) return _proc is { HasExited: false }; }
    }

    public static bool IsValidMapName(string? name) => name is not null && MapName().IsMatch(name);

    public static string? Launch(string map)
    {
        lock (Gate)
        {
            if (_proc is { HasExited: false }) return "Server already running";
            if (!File.Exists(AppPaths.Cs2Exe))
                return "CS2 server not installed. Use the Download tab first.";
            if (!IsValidMapName(map)) return "Invalid map name";

            string[] args =
            [
                AppPaths.Cs2Exe, "-dedicated",
                "+map", map,
                "+game_type", "0", "+game_mode", "1",
                "+sv_cheats", "1", "+sv_lan", "0",
                "-console", "-port", "27015",
            ];

            var bat = Path.Combine(Path.GetTempPath(), $"cs2prak_{Environment.ProcessId}.bat");
            File.WriteAllText(bat, "@echo off\r\n" + CommandLine(args) + "\r\n");

            ConsoleHwnd = IntPtr.Zero;
            var before = Native.EnumVisibleWindows();

            try
            {
                _proc = SafeProcess.Start($"cmd.exe /c \"{bat}\"", AppPaths.Cs2Dir);
            }
            catch (Exception e)
            {
                return $"Failed to start CS2: {e.Message}";
            }

            var watched = _proc;
            new Thread(() => FindConsoleAfterLaunch(watched, before))
            {
                IsBackground = true,
                Name = "cs2-console-finder",
            }.Start();

            return null;
        }
    }

    public static bool Kill()
    {
        bool wasRunning;
        lock (Gate)
        {
            ConsoleHwnd = IntPtr.Zero;
            wasRunning = _proc is { HasExited: false };
            if (wasRunning)
            {
                try
                {
                    using var tk = Process.Start(new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/f /t /pid {_proc!.Id}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                    tk?.WaitForExit(5000);
                }
                catch (Exception)
                {
                    try { _proc!.Terminate(); } catch (Exception) { }
                }
            }
            _proc?.Dispose();
            _proc = null;
        }
        OnStopped?.Invoke();
        return wasRunning;
    }

    private static void FindConsoleAfterLaunch(SafeProcess proc, HashSet<IntPtr> before)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (proc.HasExited) return;
            var now = Native.EnumVisibleWindows();
            now.ExceptWith(before);
            if (now.Count > 0)
            {
                ConsoleHwnd = now.First();
                return;
            }
            Thread.Sleep(1000);
        }
    }

    private static string CommandLine(IReadOnlyList<string> args)
    {
        var sb = new StringBuilder();
        foreach (var a in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (a.Length > 0 && a.IndexOfAny([' ', '\t', '"']) < 0) { sb.Append(a); continue; }
            sb.Append('"');
            int slashes = 0;
            foreach (var c in a)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1).Append('"'); }
                else { sb.Append('\\', slashes).Append(c); }
                slashes = 0;
            }
            sb.Append('\\', slashes * 2).Append('"');
        }
        return sb.ToString();
    }

    private sealed class SafeProcess : IDisposable
    {
        private const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
        private const uint STARTF_USESHOWWINDOW     = 0x00000001;
        private const uint STILL_ACTIVE             = 259;

        private IntPtr _handle;
        public int Id { get; }

        private SafeProcess(IntPtr handle, int id) { _handle = handle; Id = id; }

        public bool HasExited
        {
            get
            {
                if (_handle == IntPtr.Zero) return true;
                return !GetExitCodeProcess(_handle, out uint code) || code != STILL_ACTIVE;
            }
        }

        public void Terminate()
        {
            if (_handle != IntPtr.Zero) TerminateProcess(_handle, 1);
        }

        public static SafeProcess Start(string commandLine, string workingDir)
        {
            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = STARTF_USESHOWWINDOW,
                wShowWindow = 0,
            };

            var cmd = new StringBuilder(commandLine);
            if (!CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, false,
                                CREATE_NEW_PROCESS_GROUP, IntPtr.Zero, workingDir,
                                ref si, out PROCESS_INFORMATION pi))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            CloseHandle(pi.hThread);
            return new SafeProcess(pi.hProcess, (int)pi.dwProcessId);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero) { CloseHandle(_handle); _handle = IntPtr.Zero; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public uint dwProcessId, dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public IntPtr lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
            public uint dwFlags;
            public ushort wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(
            string? lpApplicationName, StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
            uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr h, out uint code);
        [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr h, uint code);
    }
}
