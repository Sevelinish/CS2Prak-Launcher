using Cs2Prak.Core;

namespace Cs2Prak.App.Shell;

internal sealed class TrayShell : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayShell(Action onOpen)
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open CS2 Practice", null, (_, _) => onOpen())
        {
            Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold),
        };
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripMenuItem("Open Console", null, (_, _) => ConsoleWatcher.ShowConsole()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => AppLifetime.Shutdown()));

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = AppInfo.WindowTitle,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) onOpen(); };
    }

    public void Hide() => _icon.Visible = false;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static Icon LoadIcon()
    {
        var png = Path.Combine(AppPaths.StaticDir, "tray_icon.png");
        if (File.Exists(png))
        {
            try
            {
                using var bmp = new Bitmap(png);
                var handle = bmp.GetHicon();
                try
                {
                    using var borrowed = Icon.FromHandle(handle);
                    return (Icon)borrowed.Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
            catch (Exception) {  }
        }

        var ico = SplashForm.IconPath();
        if (ico.Length > 0)
        {
            try { return new Icon(ico, SystemInformation.SmallIconSize); }
            catch (Exception) { }
        }
        return SystemIcons.Application;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
