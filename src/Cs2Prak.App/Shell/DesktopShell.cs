using Cs2Prak.Core;

namespace Cs2Prak.App.Shell;

internal sealed class DesktopShell
{
    private SplashForm? _splash;
    private MainForm? _main;
    private TrayShell? _tray;
    private ApplicationContext? _ctx;

    public void Start()
    {
        _splash = new SplashForm();
        _ctx = new ApplicationContext(_splash);

        _splash.Shown += (_, _) => _ = BootAsync();

        Application.Run(_ctx);

        _tray?.Dispose();
    }

    private async Task BootAsync()
    {
        try
        {
            Step("starting local services");
            Services.Boot();

            Step("waiting for the interface");
            await Services.WaitForServer();

            Step("opening");
            OpenMain();
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    private void Step(string text)
    {
        var splash = _splash;
        if (splash is null || splash.IsDisposed) return;
        try { splash.BeginInvoke(() => splash.Step(text)); }
        catch (InvalidOperationException) {  }
    }

    private void OpenMain()
    {
        var splash = _splash;
        if (splash is null || splash.IsDisposed) return;

        splash.BeginInvoke(() =>
        {
            _main = new MainForm(
                onQuit: AppLifetime.Shutdown,
                onHidden: () => { },
                onReady: OnMainReady,
                onFailed: Fail);

            _main.Show();

            _ctx!.MainForm = _main;

            _splash = null;
            try { splash.Close(); } catch (Exception) { }
            splash.Dispose();
        });
    }

    private void OnMainReady()
    {
        WebView2Runtime.ClearFailure();

        _tray = new TrayShell(() => _main?.ShowFromTray());
        AppLifetime.BeforeExit = () => _tray?.Hide();
    }

    private void Fail(Exception e)
    {
        WebView2Runtime.RecordFailure(e);

        void Report()
        {
            MessageBox.Show(
                "CS2 Practice Server could not open its window.\n\n"
                + WebView2Runtime.Summarize(e)
                + "\n\nThe full details are in desktop_error.log next to cs2prak.exe.",
                AppInfo.WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);

            _main?.Dispose();
            _main = null;
            _splash?.Dispose();
            _splash = null;
            Application.ExitThread();
        }

        var pump = (Form?)_splash ?? _main;
        if (pump is null || pump.IsDisposed) { Report(); return; }
        try { pump.BeginInvoke(Report); }
        catch (InvalidOperationException) { Report(); }
    }
}
