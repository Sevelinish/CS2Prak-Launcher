using System.Runtime.InteropServices;
using System.Text.Json;
using Cs2Prak.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Cs2Prak.App.Shell;

internal sealed class SplashForm : Form
{
    private const int CS_DROPSHADOW = 0x00020000;
    private static readonly Color Ground = Color.FromArgb(0x0e, 0x0d, 0x0b);

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Ground };
    private bool _ready;

    public SplashForm()
    {
        Text = AppInfo.SplashTitle;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Ground;
        TopMost = true;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;

        Controls.Add(_web);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        var scale = DpiScale.For(Handle);
        Size = new Size((int)(380 * scale), (int)(300 * scale));
        Native.CenterOnScreen(Handle);

        try
        {
            var env = await WebViewEnvironment.GetAsync();
            await _web.EnsureCoreWebView2Async(env);
            var s = _web.CoreWebView2.Settings;
            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsZoomControlEnabled = false;
            _web.CoreWebView2.NavigateToString(Html(_step));
            _ready = true;
        }
        catch (Exception)
        {
        }
    }

    private string _step = "starting services";

    public void Step(string text)
    {
        _step = text;
        if (!_ready || IsDisposed || _web.CoreWebView2 is null) return;
        try
        {
            var literal = JsonSerializer.Serialize(text);
            _web.CoreWebView2.ExecuteScriptAsync(
                $"(function(e){{if(e)e.textContent={literal};}})(document.getElementById('step'))");
        }
        catch (Exception) { }
    }

    private static string Html(string step) => $$"""
        <style>
          * { box-sizing: border-box; margin: 0; }
          body {
            height: 100vh; background: #0e0d0b; color: #ece6da; overflow: hidden;
            font-family: 'Segoe UI', system-ui, sans-serif; font-weight: 600;
            user-select: none; cursor: default;
            display: flex; flex-direction: column; align-items: center; justify-content: center;
            gap: 18px; border: 1px solid #2b2823;
          }
          img { width: 88px; height: 88px; }
          .title {
            font-size: 16px; font-weight: 800; letter-spacing: 4.5px; text-transform: uppercase;
          }
          .sub {
            font-family: Consolas, 'Cascadia Mono', monospace; font-size: 10.5px; font-weight: 700;
            letter-spacing: 2.5px; color: #9c9486; text-transform: uppercase; min-height: 14px;
          }
          .rail { width: 210px; height: 2px; background: #211e19; overflow: hidden; }
          .rail i { display: block; height: 100%; width: 38%; background: #ff6a1f;
                    animation: slide 1.15s cubic-bezier(.6,0,.4,1) infinite; }
          @keyframes slide { 0% { transform: translateX(-110%); } 100% { transform: translateX(320%); } }
        </style>
        <img src="{{IconDataUri.Load(IconPath())}}" alt="">
        <div class="title">CS2 Practice Server</div>
        <div class="rail"><i></i></div>
        <div class="sub" id="step">{{System.Net.WebUtility.HtmlEncode(step)}}</div>
        """;

    public static string IconPath()
    {
        foreach (var p in new[]
                 {
                     Path.Combine(AppPaths.StaticDir, "icon.ico"),
                     Path.Combine(AppPaths.Root, "icon.ico"),
                 })
        {
            if (File.Exists(p)) return p;
        }
        return "";
    }
}

internal static class DpiScale
{
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

    public static double For(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 1.0;
        }
    }
}
