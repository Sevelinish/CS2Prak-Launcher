namespace Cs2Prak.App.Shell;

internal static class IconDataUri
{
    public static string Load(string path, int want = 256)
    {
        if (string.IsNullOrEmpty(path)) return "";
        try
        {
            var blob = File.ReadAllBytes(path);
            if (blob.Length < 6) return "";

            int count = BitConverter.ToUInt16(blob, 4);
            (int Width, byte[] Data)? best = null;

            for (int i = 0; i < count; i++)
            {
                int entry = 6 + 16 * i;
                if (entry + 16 > blob.Length) break;

                int width = blob[entry] == 0 ? 256 : blob[entry];
                int size = BitConverter.ToInt32(blob, entry + 8);
                int offset = BitConverter.ToInt32(blob, entry + 12);
                if (offset < 0 || size <= 0 || offset + size > blob.Length) continue;

                if (!(blob[offset] == 0x89 && blob[offset + 1] == 'P'
                      && blob[offset + 2] == 'N' && blob[offset + 3] == 'G')) continue;

                if (best is null || Math.Abs(width - want) < Math.Abs(best.Value.Width - want))
                    best = (width, blob[offset..(offset + size)]);
            }

            return best is null ? "" : "data:image/png;base64," + Convert.ToBase64String(best.Value.Data);
        }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
        catch (ArgumentException) { return ""; }
    }
}
