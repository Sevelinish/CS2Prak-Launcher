using Cs2Prak.App.Shell;
using Cs2Prak.Core;

namespace Cs2Prak.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
