using System.Formats.Tar;
using System.IO.Compression;

namespace Cs2Prak.Core.Plugins;

public static class Archives
{
    public static void ExtractSafely(string archivePath, string destination)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        AppPaths.EnsureDir(root);

        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            ExtractTarGz(archivePath, root);
        }
        else
        {
            ExtractZip(archivePath, root);
        }
    }

    private static string? SafeTarget(string root, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return null;

        var relative = entryName.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative)) return null;

        string full;
        try { full = Path.GetFullPath(Path.Combine(root, relative)); }
        catch (Exception) { return null; }

        var trimmed = Path.TrimEndingDirectorySeparator(full);
        if (trimmed.Equals(root, StringComparison.OrdinalIgnoreCase)) return full;
        return trimmed.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? full
            : null;
    }

    private static void ExtractZip(string archivePath, string root)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        foreach (var entry in zip.Entries)
        {
            var target = SafeTarget(root, entry.FullName);
            if (target is null) continue;

            if (entry.Name.Length == 0)
            {
                AppPaths.EnsureDir(target);
                continue;
            }

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) AppPaths.EnsureDir(dir);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void ExtractTarGz(string archivePath, string root)
    {
        using var file = File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        while (tar.GetNextEntry() is { } entry)
        {
            var target = SafeTarget(root, entry.Name);
            if (target is null) continue;

            if (entry.EntryType is TarEntryType.Directory)
            {
                AppPaths.EnsureDir(target);
                continue;
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                continue;

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) AppPaths.EnsureDir(dir);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
