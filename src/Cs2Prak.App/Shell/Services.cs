using Cs2Prak.Core;
using Cs2Prak.Core.MySql;
using Cs2Prak.Core.Plugins;
using Cs2Prak.Core.Skins;
using Cs2Prak.Core.Demos;
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

        Overlay.OnRebuilt = PluginInstaller.ReinstallAfterRebuild;

        Cs2ServerProcess.OnStopped = () => Overlay.RemoveCssBasePathLink();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Overlay.RemoveCssBasePathLink();
    }

    public static Task<bool> WaitForServer() => ApiHost.WaitUntilReady(TimeSpan.FromSeconds(40));
}
