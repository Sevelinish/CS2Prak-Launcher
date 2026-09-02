using Cs2Prak.Core;

namespace Cs2Prak.App.Shell;

internal static class ConsoleWatcher
{
    public static void Start()
    {
        new Thread(Loop)
        {
            IsBackground = true,
            Name = "cs2-console-watch",
        }.Start();
    }

    private static void Loop()
    {
        while (true)
        {
            var hwnd = Cs2ServerProcess.ConsoleHwnd;
            if (hwnd != IntPtr.Zero)
            {
                try
                {
                    var style = Native.GetWindowLongW(hwnd, Native.GWL_STYLE);
                    if ((style & Native.WS_MINIMIZE) != 0) Native.ShowWindow(hwnd, Native.SW_HIDE);
                }
                catch (Exception) {  }
            }
            Thread.Sleep(150);
        }
    }

    public static void ShowConsole()
    {
        var hwnd = Cs2ServerProcess.ConsoleHwnd;
        if (hwnd == IntPtr.Zero) return;
        Native.ShowWindow(hwnd, Native.SW_RESTORE);
        Native.SetForegroundWindow(hwnd);
    }
}
