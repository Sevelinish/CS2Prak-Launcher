# CS2Prak Launcher

A local practice-server launcher and companion for Counter-Strike 2 on Windows.
Create a dedicated server, apply weapon skins, manage plugins, generate binds and
replay your demos in a built-in 2D viewer, all from one desktop window.

The app is a .NET 9 desktop application. The interface is a WebView2 window backed
by a local web server that only ever listens on loopback (`127.0.0.1:5000`).
Everything runs on your machine: demos are parsed locally and nothing is uploaded.

## Features

**Server.** Installs a CS2 dedicated server through SteamCMD, launches it on a map
from the FACEIT pool, and gives you the connect string or a direct join into CS2.

**Skins.** Configures WeaponPaints loadouts: skins, knives, gloves, agents, wear,
seed, name tags, StatTrak and stickers. A built-in MySQL-over-SQLite shim serves
the plugin, so no MySQL or XAMPP install is needed. Skins can also be pulled from
an HLTV player profile.

**Plugins.** Installs and updates Metamod:Source, CounterStrikeSharp, MatchZy,
WeaponPaints, PlayerSettings, MenuManagerCS2 and AnyBaseLibCS2 straight from their
GitHub releases. Installed plugins can be switched on and off.

**Binds.** Binds plugin chat commands to keys and exports `sBinds.cfg`.

**Demo viewer.** Reads `.dem`, `.dem.gz` and `.dem.zst` into a 2D radar replay with
a kill feed, voice, grenade trajectories, smokes, molotovs, HE and flashes.

**Statistics.** A scoreboard with HLTV 2.0 rating, KAST, ADR and per-round detail,
plus scouting numbers the scoreboard cannot show: opening duels, trades, clutches,
utility and buy discipline.

**Advanced.** Per-player duel analysis, tick by tick: reaction time, crosshair
placement, first-bullet accuracy, counter-strafe, distance and flash state.

The interface is available in English and Russian.

## Requirements

- Windows 10 or 11, x64
- Microsoft WebView2 runtime. It ships with Windows 11 and arrives with Edge on
  Windows 10. The app offers a download link if it is missing.
- A free FACEIT API key to unlock the Analytics tabs (demo viewer, statistics,
  advanced). The rest of the app works without one.
- Disk space for the dedicated server, which SteamCMD downloads on first use.

No .NET install is required. Releases ship with the runtime inside.

## Install

Download the latest `cs2prak-<version>-win-x64.zip` from the
[releases page](https://github.com/Sevelinish/CS2Prak-Launcher/releases), unpack it
anywhere and run `cs2prak.exe`. The launcher updates itself from there on.

## Build from source

Needs the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
dotnet build Cs2Prak.sln
```

The result lands in `src/Cs2Prak.App/bin/Debug/net9.0-windows/cs2prak.exe`. This
build needs the .NET 9 runtime present on the machine.

For a build that runs anywhere:

```bash
dotnet publish src/Cs2Prak.App/Cs2Prak.App.csproj -c Release -r win-x64 --self-contained true -o publish
```

## Project layout

```
assets/     frontend (static, templates), map thumbnails, icon
src/
  Cs2Prak.Core/     paths, Win32, server process, plugins, skins,
                    MySQL shim, demo parsing, updates, uninstall
  Cs2Prak.Server/   local web server and API routes
  Cs2Prak.App/      splash, WebView2 window, tray
```

Assets are copied next to the executable at build time, which is where the app
looks for them. They are copied rather than embedded so a patch release can
replace a `.css` or a `.js` without a rebuild.

## Releases

Versions run `1.0.01` through `1.0.99`, then `1.1.01`. The number lives in
`src/Cs2Prak.Core/AppInfo.cs` next to `UpdateRepo`, which tells the launcher where
to look for updates.

A release carries three assets: the full install archive, an incremental
`update.zip` and a `manifest.json` of file hashes. The launcher downloads the
manifest, compares hashes against the local files and fetches only what differs.

## License

MIT. See [LICENSE](LICENSE).
