using System.IO.Compression;

namespace DotNetHttpFile.Tests.Support;

/// <summary>Compresses test content the way a server would.</summary>
internal static class Compression
{
    public static byte[] Gzip(ReadOnlySpan<byte> content)
    {
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(content);
        }

        return output.ToArray();
    }

    public static byte[] Zlib(ReadOnlySpan<byte> content)
    {
        using MemoryStream output = new();
        using (ZLibStream zlib = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(content);
        }

        return output.ToArray();
    }

    public static byte[] RawDeflate(ReadOnlySpan<byte> content)
    {
        using MemoryStream output = new();
        using (DeflateStream deflate = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(content);
        }

        return output.ToArray();
    }

    public static byte[] Brotli(ReadOnlySpan<byte> content)
    {
        using MemoryStream output = new();
        using (BrotliStream brotli = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(content);
        }

        return output.ToArray();
    }

    /// <summary>Gzip of <paramref name="length"/> zero bytes, written in chunks so the content is never held.</summary>
    public static byte[] GzipZeros(long length)
    {
        byte[] chunk = new byte[1024 * 1024];
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (long remaining = length; remaining > 0; remaining -= chunk.Length)
            {
                gzip.Write(chunk, 0, (int)Math.Min(chunk.Length, remaining));
            }
        }

        return output.ToArray();
    }
}
