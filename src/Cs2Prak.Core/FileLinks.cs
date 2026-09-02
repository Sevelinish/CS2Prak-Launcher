using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cs2Prak.Core;

public static class FileLinks
{
    public static bool IsUnder(string path, string root)
    {
        try
        {
            var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (a.Equals(r, StringComparison.OrdinalIgnoreCase)) return true;
            return a.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception) { return false; }
    }

    public static bool IsDirectoryEntry(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.Directory) != 0; }
        catch (Exception) { return false; }
    }

    public static bool IsPlainDirectory(string path)
    {
        if (IsLink(path)) return false;
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) != 0;
        }
        catch (Exception) { return false; }
    }

    public static int HardLinkCount(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(handle, out var info) ? (int)info.NumberOfLinks : 0;
        }
        catch (Exception) { return 0; }
    }

    public static string? Target(string path)
    {
        try
        {
            var info = IsDirectoryEntry(path)
                ? new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)
                : new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true);
            return info?.FullName;
        }
        catch (Exception) { return null; }
    }

    public static bool CreateJunction(string link, string target)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add("mklink");
            psi.ArgumentList.Add("/J");
            psi.ArgumentList.Add(link);
            psi.ArgumentList.Add(target);

            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(15000);
            return Directory.Exists(link);
        }
        catch (Exception) { return false; }
    }

    public static bool TryHardLink(string existing, string link)
    {
        try { return CreateHardLinkW(link, existing, IntPtr.Zero); }
        catch (Exception) { return false; }
    }

    public static bool? SameFile(string a, string b)
    {
        try
        {
            using var ha = File.OpenHandle(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var hb = File.OpenHandle(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (!GetFileInformationByHandle(ha, out var ia)) return null;
            if (!GetFileInformationByHandle(hb, out var ib)) return null;
            return ia.VolumeSerialNumber == ib.VolumeSerialNumber
                   && ia.FileIndexHigh == ib.FileIndexHigh
                   && ia.FileIndexLow == ib.FileIndexLow;
        }
        catch (Exception) { return null; }
    }

    public static bool RemoveLink(string path, out string? error)
    {
        error = null;
        try
        {
            if (IsLink(path) && IsDirectoryEntry(path))
            {
                Directory.Delete(path);
                return true;
            }
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            return false;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string newFileName, string existingFileName,
                                               IntPtr securityAttributes);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public long CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks,
                    FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle,
                                                          out BY_HANDLE_FILE_INFORMATION info);
}
