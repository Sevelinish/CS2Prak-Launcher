using System.Runtime.InteropServices;
using Cs2Prak.Core;
using Cs2Prak.Core.MySql;
using Cs2Prak.Core.Plugins;
using Cs2Prak.Core.Skins;
using Cs2Prak.Core.Demos;
using Cs2Prak.Core.Highlights;
using Cs2Prak.Core.Update;
using Cs2Prak.Server;
using Cs2Prak.Server.Endpoints;
using Microsoft.AspNetCore.Builder;

namespace Cs2Prak.App.Shell;

internal static class Services
{
    private static bool _booted;
    private static WebApplication? _web;

    public static void Boot()
    {
        if (_booted) return;
        _booted = true;

        MySqlSqliteServer.Start();

        SkinsDatabase.EnsureSchema();
        Catalogues.Reload();

        WireModules();

        _web = ApiHost.Create();
        _web.RunAsync();

        Updater.StartCheck();

        DemoParsing.Parse = CsDemoAnalyzer.Parse;
        ParseQueue.Start();
        ConsoleWatcher.Start();
    }

    private static void WireModules()
    {
        Overlay.RemoveCssBasePathLink();

        ServerConfigurator.EnsureSkinsSchema = SkinsDatabase.EnsureSchema;
        ServerConfigurator.ReloadCatalogues = Catalogues.Reload;

        ServerEndpoints.BeforeLaunch = () =>
        {
            ServerConfigurator.PatchWeaponPaintsConfig();
            ServerConfigurator.ConfigureWeaponPaintsDb();
            SkinsDatabase.EnsureSchema();

            if (Directory.Exists(AppPaths.CssBase)) Overlay.EnsureCssBasePathLink();
        };

        Overlay.OnRebuilt = log =>
        {
            PluginInstaller.ReinstallAfterRebuild(log);
            PluginUpdates.Refresh();
        };

        PluginUpdates.Start();

        HighlighterIcons.FromExecutable = IconOfExecutable;
        HighlighterIcons.Warm();

        Cs2ServerProcess.OnStopped = () => Overlay.RemoveCssBasePathLink();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Overlay.RemoveCssBasePathLink();
            HighlighterClient.Shutdown();
        };
    }

    private static byte[]? IconOfExecutable(string exe)
    {
        var handles = new IntPtr[1];
        var ids = new int[1];

        foreach (var size in new[] { 48, 32, 16 })
        {
            if (PrivateExtractIconsW(exe, 0, size, size, handles, ids, 1, 0) < 1) continue;
            if (handles[0] == IntPtr.Zero) continue;

            try
            {
                using var icon = System.Drawing.Icon.FromHandle(handles[0]);
                using var bitmap = icon.ToBitmap();
                using var buffer = new MemoryStream();
                bitmap.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
                return buffer.ToArray();
            }
            catch (Exception) { return null; }
            finally { DestroyIcon(handles[0]); }
        }

        return null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int PrivateExtractIconsW(string file, int index, int cx, int cy,
                                                   IntPtr[] icons, int[] ids, int count, int flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    public static Task<bool> WaitForServer() => ApiHost.WaitUntilReady(TimeSpan.FromSeconds(40));
}
