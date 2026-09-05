namespace Cs2Prak.Core.Plugins;

public enum VersionSource
{
    Dll,

    CssDeps,

    Tracker,
}

public sealed record PluginDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    public required string GitHub { get; init; }

    public string? GitHubTagPrefix { get; init; }

    public required string Marker { get; init; }

    public required VersionSource VersionSrc { get; init; }

    public string[] DependsOn { get; init; } = [];

    public string? AssetNamePrefer { get; init; }

    public string[] AssetNameExclude { get; init; } = [];

    public required string ExtractTo { get; init; }

    public string[] Preserve { get; init; } = [];

    public bool IsDependency { get; init; }

    public string ReleasesUrl => $"https://github.com/{GitHub}/releases";

    public bool IsInstalled => File.Exists(Marker);
}

public static class PluginCatalog
{
    public static IReadOnlyList<PluginDef> All { get; } = Build();

    public static PluginDef? Find(string id) =>
        All.FirstOrDefault(p => p.Id == id);

    public static PluginDef Require(string id) =>
        Find(id) ?? throw new InvalidOperationException($"Unknown plugin: {id}");

    private static IReadOnlyList<PluginDef> Build()
    {
        var csgo = AppPaths.CsgoBase;
        var addons = AppPaths.CsgoAddons;
        var css = AppPaths.CssBase;
        var plugins = AppPaths.CssPlugins;

        return
        [
            new PluginDef
            {
                Id = "metamod",
                Name = "Metamod:Source",
                Description = "Plugin loader for CS2 — required by CounterStrikeSharp",
                GitHub = "alliedmodders/metamod-source",
                GitHubTagPrefix = "2.",
                Marker = Path.Combine(addons, @"metamod\bin\win64\metamod.2.cs2.dll"),
                VersionSrc = VersionSource.Dll,
                ExtractTo = csgo,
            },
            new PluginDef
            {
                Id = "counterstrikesharp",
                Name = "CounterStrikeSharp",
                Description = "C# plugin framework built on Metamod:Source",
                GitHub = "roflmuffin/CounterStrikeSharp",
                Marker = Path.Combine(addons, @"counterstrikesharp\bin\win64\counterstrikesharp.dll"),
                VersionSrc = VersionSource.CssDeps,
                DependsOn = ["metamod"],
                AssetNamePrefer = "with-runtime",
                ExtractTo = csgo,
                Preserve =
                [
                    Path.Combine("addons", "counterstrikesharp", "configs"),
                    Path.Combine("addons", "counterstrikesharp", "plugins"),
                ],
            },
            new PluginDef
            {
                Id = "anybaselibcs2",
                Name = "AnyBaseLibCS2",
                Description = "Base library required by PlayerSettings and MenuManager",
                GitHub = "NickFox007/AnyBaseLibCS2",
                Marker = Path.Combine(css, @"shared\AnyBaseLib\AnyBaseLib.dll"),
                VersionSrc = VersionSource.Tracker,
                DependsOn = ["counterstrikesharp"],
                ExtractTo = csgo,
                IsDependency = true,
            },
            new PluginDef
            {
                Id = "playersettings",
                Name = "PlayerSettings",
                Description = "Player settings storage required by MenuManager",
                GitHub = "NickFox007/PlayerSettingsCS2",
                Marker = Path.Combine(plugins, @"PlayerSettings\PlayerSettings.dll"),
                VersionSrc = VersionSource.Tracker,
                DependsOn = ["counterstrikesharp", "anybaselibcs2"],
                ExtractTo = csgo,
                IsDependency = true,
            },
            new PluginDef
            {
                Id = "menumanagercs2",
                Name = "MenuManagerCS2",
                Description = "In-game menu system required by WeaponPaints",
                GitHub = "NickFox007/MenuManagerCS2",
                Marker = Path.Combine(plugins, @"MenuManagerCore\MenuManagerCore.dll"),
                VersionSrc = VersionSource.Tracker,
                DependsOn = ["counterstrikesharp", "anybaselibcs2", "playersettings"],
                ExtractTo = csgo,
                IsDependency = true,
            },
            new PluginDef
            {
                Id = "matchzy",
                Name = "MatchZy",
                Description = "Practice and match management plugin",
                GitHub = "shobhit-pathak/MatchZy",
                Marker = Path.Combine(plugins, @"MatchZy\MatchZy.dll"),
                VersionSrc = VersionSource.Tracker,
                DependsOn = ["counterstrikesharp"],
                AssetNameExclude = ["with-cssharp"],
                ExtractTo = csgo,
                Preserve =
                [
                    Path.Combine("addons", "counterstrikesharp", "plugins", "MatchZy", "lang"),
                    Path.Combine("addons", "counterstrikesharp", "plugins", "MatchZy", "matchzy.db"),
                    Path.Combine("addons", "counterstrikesharp", "plugins", "MatchZy", "spawns"),
                ],
            },
            new PluginDef
            {
                Id = "weaponpaints",
                Name = "WeaponPaints",
                Description = "Weapon skin and glove customization for players",
                GitHub = "Nereziel/cs2-WeaponPaints",
                Marker = Path.Combine(plugins, @"WeaponPaints\WeaponPaints.dll"),
                VersionSrc = VersionSource.Tracker,
                DependsOn = ["anybaselibcs2", "playersettings", "menumanagercs2"],
                AssetNameExclude = ["website"],
                ExtractTo = plugins,
                Preserve = [Path.Combine("WeaponPaints", "lang")],
            },
            new PluginDef
            {
                Id = "timerhud",
                Name = "TimerHUD",
                Description = "Run timer under the crosshair for surf and bhop practice",
                GitHub = "Sevelinish/CS2TimerHUD",
                Marker = Path.Combine(plugins, @"TimerHUD\TimerHUD.dll"),
                VersionSrc = VersionSource.Tracker,
                DependsOn = ["counterstrikesharp"],
                ExtractTo = Path.Combine(plugins, "TimerHUD"),
            },
            new PluginDef
            {
                Id = "movementhud",
                Name = "MovementHUD",
                Description = "On-screen keystroke indicator for movement practice",
                GitHub = "Sevelinish/CS2MovementHUD",
                Marker = Path.Combine(plugins, @"MovementHUD\MovementHUD.dll"),
                VersionSrc = VersionSource.Tracker,
                DependsOn = ["counterstrikesharp"],
                ExtractTo = Path.Combine(plugins, "MovementHUD"),
            },
        ];
    }
}
