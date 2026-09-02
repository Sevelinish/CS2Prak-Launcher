using System.Runtime.InteropServices;

namespace Cs2Prak.Core;

public static class Native
{
    public const int GWL_STYLE   = -16;
    public const int GWLP_WNDPROC = -4;

    public const int WS_THICKFRAME  = 0x00040000;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_MAXIMIZEBOX = 0x00010000;
    public const int WS_MINIMIZE    = 0x20000000;

    public const uint SWP_NOSIZE       = 0x0001;
    public const uint SWP_NOMOVE       = 0x0002;
    public const uint SWP_NOZORDER     = 0x0004;
    public const uint SWP_FRAMECHANGED = 0x0020;

    public const int SW_HIDE     = 0;
    public const int SW_MAXIMIZE = 3;
    public const int SW_SHOW     = 5;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE  = 9;

    public const uint WM_NCCALCSIZE = 0x0083;

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NCCALCSIZE_PARAMS
    {
        public RECT rgrc0, rgrc1, rgrc2;
        public IntPtr lppos;
    }

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    public delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern int GetWindowTextLengthW(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hwnd, char[] buf, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rc);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfoW(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] public static extern int GetWindowLongW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLongW(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr CallWindowProcW(IntPtr prev, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] public static extern bool SetPriorityClass(IntPtr handle, uint cls);

    public const uint IDLE_PRIORITY_CLASS = 0x00000040;

    public static void LowerOwnPriority()
    {
        try { SetPriorityClass(GetCurrentProcess(), IDLE_PRIORITY_CLASS); }
        catch (Exception) { }
    }

    public static HashSet<IntPtr> EnumVisibleWindows()
    {
        var found = new HashSet<IntPtr>();
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h)) found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr OwnWindow(string title)
    {
        var mine = (uint)Environment.ProcessId;
        IntPtr hit = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (pid != mine) return true;
            int n = GetWindowTextLengthW(h);
            if (n <= 0) return true;
            var buf = new char[n + 1];
            int got = GetWindowTextW(h, buf, n + 1);
            if (got > 0 && new string(buf, 0, got) == title) { hit = h; return false; }
            return true;
        }, IntPtr.Zero);
        return hit;
    }

    public static void CenterOnScreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        if (!GetWindowRect(hwnd, out RECT rc)) return;
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;

        if (!GetCursorPos(out POINT pt)) return;
        IntPtr mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(mon, ref mi)) return;

        RECT area = mi.rcWork;
        int x = area.Left + ((area.Right - area.Left) - w) / 2;
        int y = area.Top + ((area.Bottom - area.Top) - h) / 2;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    }
}
