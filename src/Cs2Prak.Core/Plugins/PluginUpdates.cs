namespace Cs2Prak.Core.Plugins;

public sealed record PluginUpdate(string id, string name, string local, string latest);

public static class PluginUpdates
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(20);

    private static readonly Lock Gate = new();
    private static readonly AutoResetEvent Wake = new(false);

    private static List<PluginUpdate> _outdated = [];
    private static DateTimeOffset? _checked;
    private static bool _running;
    private static bool _started;

    public static IReadOnlyList<PluginUpdate> Outdated
    {
        get { lock (Gate) return _outdated; }
    }

    public static DateTimeOffset? LastChecked
    {
        get { lock (Gate) return _checked; }
    }

    public static bool Running
    {
        get { lock (Gate) return _running; }
    }

    public static void Start()
    {
        lock (Gate)
        {
            if (_started) return;
            _started = true;
        }
        new Thread(Loop) { IsBackground = true, Name = "plugin-update-check" }.Start();
    }

    public static void Refresh() => Wake.Set();

    private static void Loop()
    {
        Wake.WaitOne(Settle);

        while (true)
        {
            if (File.Exists(AppPaths.Cs2Exe)) Scan();
            Wake.WaitOne(Every);
        }
    }

    private static void Scan()
    {
        lock (Gate)
        {
            if (_running) return;
            _running = true;
        }

        try
        {
            var state = PluginState.Load();
            var found = new List<PluginUpdate>();

            foreach (var plugin in PluginCatalog.All)
            {
                if (plugin.SkipUpdateCheck || !plugin.IsInstalled) continue;

                var local = PluginState.LocalVersion(plugin, state);
                if (local is null || local == PluginState.Unknown) continue;

                var release = GitHubReleases.Latest(plugin.GitHub, TimeSpan.FromSeconds(8),
                                                    plugin.GitHubTagPrefix);
                if (release is null) continue;

                if (ReleaseVersion.IsNewer(release.TagName, local))
                    found.Add(new PluginUpdate(plugin.Id, plugin.Name, local, release.TagName));
            }

            lock (Gate)
            {
                _outdated = found;
                _checked = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            lock (Gate) _running = false;
        }
    }
}
