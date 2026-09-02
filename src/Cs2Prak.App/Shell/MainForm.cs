using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cs2Prak.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Cs2Prak.App.Shell;

internal sealed class MainForm : Form
{
    private static readonly Color Ground = Color.FromArgb(0x0e, 0x0d, 0x0b);

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Ground };
    private readonly Action _onQuit;
    private readonly Action _onHidden;
    private readonly Action _onReady;
    private readonly Action<Exception> _onFailed;

    public MainForm(Action onQuit, Action onHidden, Action onReady, Action<Exception> onFailed)
    {
        _onQuit = onQuit;
        _onHidden = onHidden;
        _onReady = onReady;
        _onFailed = onFailed;

        Text = AppInfo.WindowTitle;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Ground;
        StartPosition = FormStartPosition.Manual;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var iconPath = SplashForm.IconPath();
        if (iconPath.Length > 0)
        {
            try { Icon = new Icon(iconPath); }
            catch (Exception) {  }
        }

        Controls.Add(_web);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= Native.WS_THICKFRAME | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_NCCALCSIZE && m.WParam != IntPtr.Zero && !Native.IsZoomed(Handle))
        {
            var before = Marshal.PtrToStructure<Native.NCCALCSIZE_PARAMS>(m.LParam);
            var windowTop = before.rgrc0.Top;

            base.WndProc(ref m);

            var after = Marshal.PtrToStructure<Native.NCCALCSIZE_PARAMS>(m.LParam);
            after.rgrc0.Top = windowTop;
            Marshal.StructureToPtr(after, m.LParam, false);
            return;
        }

        base.WndProc(ref m);
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        var scale = DpiScale.For(Handle);
        Size = new Size((int)(1280 * scale), (int)(830 * scale));
        MinimumSize = new Size((int)(1024 * scale), (int)(680 * scale));
        Native.CenterOnScreen(Handle);

        try
        {
            var env = await WebViewEnvironment.GetAsync();
            await _web.EnsureCoreWebView2Async(env);

            var core = _web.CoreWebView2;
            var s = core.Settings;

            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsZoomControlEnabled = false;
            s.IsSwipeNavigationEnabled = false;

            core.WebMessageReceived += OnWebMessage;
            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationStarting += OnNavigationStarting;

            await core.AddScriptToExecuteOnDocumentCreatedAsync(PywebviewShim.Script);
            core.Navigate(AppInfo.HomeUrl);

            _onReady();
        }
        catch (Exception ex)
        {
            _onFailed(ex);
        }
    }

    private static void OnNewWindowRequested(object? _, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternally(e.Uri);
    }

    private void OnNavigationStarting(object? _, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
        if (uri.IsLoopback) return;
        if (uri.Scheme is not ("http" or "https")) return;

        e.Cancel = true;
        OpenExternally(e.Uri);
    }

    private static void OpenExternally(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });
        }
        catch (Exception) { }
    }

    private void OnWebMessage(object? _, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonNode? msg;
        try { msg = JsonNode.Parse(e.TryGetWebMessageAsString() ?? ""); }
        catch (JsonException) { return; }
        catch (ArgumentException) { return; }
        if (msg is null) return;

        var kind = (string?)msg["kind"];

        if (kind == "move")
        {
            MoveTo((double?)msg["x"] ?? 0, (double?)msg["y"] ?? 0);
            return;
        }

        if (kind != "api") return;

        var id = (string?)msg["id"];
        var name = (string?)msg["name"];
        if (id is null || name is null) return;

        object? value = name switch
        {
            "minimize"        => Minimize(),
            "toggle_maximize" => ToggleMaximize(),
            "is_maximized"    => Native.IsZoomed(Handle),
            "hide_to_tray"    => HideToTray(),
            "show"            => ShowFromTray(),
            "quit"            => Quit(),
            _                 => null,
        };

        var reply = new JsonObject
        {
            ["kind"] = "reply",
            ["id"] = id,
            ["value"] = value is null ? null : JsonValue.Create(value),
        };
        _web.CoreWebView2.PostWebMessageAsJson(reply.ToJsonString());
    }

    private void MoveTo(double x, double y)
    {
        var scale = DpiScale.For(Handle);
        Native.SetWindowPos(Handle, IntPtr.Zero, (int)(x * scale), (int)(y * scale), 0, 0,
                            Native.SWP_NOSIZE | Native.SWP_NOZORDER);
    }

    private bool Minimize()
    {
        Native.ShowWindow(Handle, Native.SW_MINIMIZE);
        return true;
    }

    private bool ToggleMaximize()
    {
        var zoomed = Native.IsZoomed(Handle);
        Native.ShowWindow(Handle, zoomed ? Native.SW_RESTORE : Native.SW_MAXIMIZE);
        return !zoomed;
    }

    public bool HideToTray()
    {
        Hide();
        _onHidden();
        return true;
    }

    public bool ShowFromTray()
    {
        Show();
        Native.ShowWindow(Handle, Native.SW_SHOW);
        if (Native.IsIconic(Handle)) Native.ShowWindow(Handle, Native.SW_RESTORE);
        Native.SetForegroundWindow(Handle);
        return true;
    }

    private bool Quit()
    {
        _onQuit();
        return true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnFormClosing(e);
    }
}
