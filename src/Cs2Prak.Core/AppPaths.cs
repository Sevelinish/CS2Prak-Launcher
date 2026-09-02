using System.Runtime.CompilerServices;

namespace Cs2Prak.Core;

public static class AppPaths
{
    public static string Root { get; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public static string Bundle => Root;

    public static string StaticDir    => Path.Combine(Bundle, "static");
    public static string TemplatesDir => Path.Combine(Bundle, "templates");
    public static string MapsDir      => Path.Combine(Root, "maps");
    public static string IndexHtml    => Path.Combine(TemplatesDir, "index.html");

    public static string DbPath          => Path.Combine(Root, "skins.db");
    public static string DemosCacheDir   => Path.Combine(Root, "demos_cache");
    public static string PluginStatePath => Path.Combine(Root, "plugin_versions.json");

    public static string ShellErrorLog => Path.Combine(Root, "desktop_error.log");
    public static string UpdateDir => Path.Combine(Path.GetTempPath(), "cs2prak_update_cs");

    public static string ServerRoot  => Path.Combine(Root, "cs2Server");
    public static string Cs2Common   => Path.Combine(ServerRoot, @"steamapps\common\Counter-Strike Global Offensive");
    public static string Cs2Game     => Path.Combine(Cs2Common, "game");
    public static string Cs2Dir      => Path.Combine(Cs2Game, @"bin\win64");
    public static string Cs2Exe      => Path.Combine(Cs2Dir, "cs2.exe");
    public static string SteamCmdDir => ServerRoot;
    public static string SteamCmd    => Path.Combine(ServerRoot, "steamcmd.exe");
    public static string GameinfoGi  => Path.Combine(Cs2Game, @"csgo\gameinfo.gi");

    public static string CsgoBase           => Path.Combine(Cs2Game, "csgo");
    public static string CsgoAddons         => Path.Combine(CsgoBase, "addons");
    public static string CssBase            => Path.Combine(CsgoAddons, "counterstrikesharp");
    public static string CssPlugins         => Path.Combine(CssBase, "plugins");
    public static string CssPluginsDisabled => Path.Combine(CssBase, "plugins_disabled");
    public static string PluginData         => Path.Combine(CssPlugins, @"WeaponPaints\data");
    public static string AdminsJson         => Path.Combine(CssBase, @"configs\admins.json");
    public static string WeaponPaintsConfig =>
        Path.Combine(CsgoAddons, @"counterstrikesharp\configs\plugins\WeaponPaints\WeaponPaints.json");
    public static string CssCoreConfig =>
        Path.Combine(CsgoAddons, @"counterstrikesharp\configs\core.json");

    public static string DownloadsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public const string SteamCmdUrl    = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
    public const string Dotnet8VerUrl  = "https://dotnetcli.azureedge.net/dotnet/Runtime/8.0/latest.version";

    public static void EnsureDir(string path)
    {
        try { Directory.CreateDirectory(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
