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
            // A recording may have left CS2 sitting on the main menu. A second CS2 cannot
            // start alongside it, so hand that one back before launching the server.
            if (HighlighterProcess.IsRunning)
                HighlighterClient.TryCall("session.release", null, TimeSpan.FromSeconds(30));

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

        Cs2ServerProcess.OnStopped = () => Overlay.RemoveCssBasePathLink();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Overlay.RemoveCssBasePathLink();
            HighlighterClient.Shutdown();
        };
    }

    public static Task<bool> WaitForServer() => ApiHost.WaitUntilReady(TimeSpan.FromSeconds(40));
}
