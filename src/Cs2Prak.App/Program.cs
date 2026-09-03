using System.Diagnostics;
using Cs2Prak.App.Shell;
using Cs2Prak.Core;

namespace Cs2Prak.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var only = new Mutex(true, @"Local\cs2prak_single_instance", out var first);
        if (!first)
        {
            FocusRunningWindow();
            return;
        }

        Native.LowerOwnPriority();

        ApplicationConfiguration.Initialize();

        if (!WebView2Runtime.IsInstalled)
        {
            var install = MessageBox.Show(
                "CS2 Practice Server needs the Microsoft WebView2 runtime, and this "
                + "machine does not have it.\n\nIt ships with Windows 11 and arrives "
                + "with Edge on Windows 10.\n\nOpen the download page?",
                "WebView2 runtime required",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (install == DialogResult.Yes) OpenUrl(WebView2Runtime.DownloadUrl);
            return;
        }

        var shell = new DesktopShell();
        shell.Start();
    }

    private static void FocusRunningWindow()
    {
        const int Restore = 9;

        foreach (var other in Process.GetProcessesByName("cs2prak"))
        {
            using (other)
            {
                if (other.Id == Environment.ProcessId) continue;
                if (other.MainWindowHandle == IntPtr.Zero) continue;

                Native.ShowWindow(other.MainWindowHandle, Restore);
                Native.SetForegroundWindow(other.MainWindowHandle);
                return;
            }
        }
    }

    internal static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception) {  }
    }
}
