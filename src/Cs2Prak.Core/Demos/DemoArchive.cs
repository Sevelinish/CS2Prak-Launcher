using System.IO.Compression;

namespace Cs2Prak.Core.Demos;

public static class DemoArchive
{
    public const long MaxRawSize = 2L * 1024 * 1024 * 1024;

    public static string ToRawDemo(string source)
    {
        Span<byte> head = stackalloc byte[4];
        using (var probe = File.OpenRead(source))
        {
            var read = probe.Read(head);
            if (read < 4) return source;
        }

        if (head[0] == 0x28 && head[1] == 0xB5 && head[2] == 0x2F && head[3] == 0xFD)
        {
            using var input = File.OpenRead(source);
            using var zstd = new ZstdSharp.DecompressionStream(input);
            return BoundedCopy(zstd, source + ".dem");
        }

        if (head[0] == 0x1F && head[1] == 0x8B)
        {
            using var input = File.OpenRead(source);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            return BoundedCopy(gzip, source + ".dem");
        }

        return source;
    }

    public static string BoundedCopy(Stream reader, string destination, long limit = MaxRawSize)
    {
        try
        {
            using (var output = File.Create(destination))
            {
                var buffer = new byte[1 << 20];
                long total = 0;
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > limit)
                        throw new InvalidOperationException(
                            $"archive expands past {limit / 1048576} MB — refusing it");
                    output.Write(buffer, 0, read);
                }
            }
            return destination;
        }
        catch
        {
            try { File.Delete(destination); } catch (Exception) { }
            throw;
        }
    }
}
