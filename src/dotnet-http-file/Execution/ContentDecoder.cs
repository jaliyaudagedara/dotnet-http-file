using System.IO.Compression;

namespace DotNetHttpFile.Execution;

/// <summary>Decodes a response body from the content codings the tool supports, gzip and deflate.</summary>
internal static class ContentDecoder
{
    /// <summary>The most bytes a body may decode to, 100 MiB.</summary>
    public const int MaxDecodedLength = MaxDecodedMebibytes * BytesPerMebibyte;

    /// <summary><see cref="MaxDecodedLength"/> in MiB, for messages.</summary>
    public const int MaxDecodedMebibytes = 100;

    private const int BytesPerMebibyte = 1024 * 1024;
    private const string GzipCoding = "gzip";
    private const string LegacyGzipCoding = "x-gzip";
    private const string DeflateCoding = "deflate";
    private const int BufferSize = 81920;

    // A first guess at the decoded size, so a typical body is not copied each time the output grows.
    private const int ExpectedExpansion = 4;
    private const int ZlibHeaderLength = 2;
    private const int ZlibMethodMask = 0x0F;
    private const int ZlibDeflateMethod = 8;
    private const int ZlibWindowShift = 4;
    private const int ZlibMaxWindow = 7;
    private const int ZlibHeaderCheckDivisor = 31;

    /// <summary>Whether <paramref name="coding"/> is one the tool decodes.</summary>
    public static bool IsSupported(string coding)
    {
        ArgumentNullException.ThrowIfNull(coding);

        return IsGzip(coding) || IsDeflate(coding);
    }

    /// <summary>
    /// Decodes <paramref name="body"/> from <paramref name="coding"/>, which must be supported. A deflate body is
    /// read as zlib-wrapped when it starts with a zlib header, and as raw deflate otherwise.
    /// </summary>
    /// <returns>
    /// The decoded bytes, or null when they would be more than <paramref name="maxDecodedLength"/>. Decoding stops
    /// at the limit.
    /// </returns>
    /// <exception cref="InvalidDataException"><paramref name="body"/> is not valid data in the coding.</exception>
    public static byte[]? Decode(
        byte[] body,
        string coding,
        int maxDecodedLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(coding);

        using MemoryStream input = new(body, writable: false);
        using Stream decoder = CreateDecoder(input, body, coding);
        using MemoryStream output = new((int)Math.Min((long)body.Length * ExpectedExpansion, maxDecodedLength));
        byte[] buffer = new byte[BufferSize];
        int read;
        while ((read = decoder.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (output.Length + read > maxDecodedLength)
            {
                return null;
            }

            output.Write(buffer, 0, read);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return output.ToArray();
    }

    private static Stream CreateDecoder(Stream input, byte[] body, string coding)
    {
        if (IsGzip(coding))
        {
            return new GZipStream(input, CompressionMode.Decompress);
        }

        if (!IsDeflate(coding))
        {
            throw new ArgumentOutOfRangeException(nameof(coding), coding, null);
        }

        return HasZlibHeader(body)
            ? new ZLibStream(input, CompressionMode.Decompress)
            : new DeflateStream(input, CompressionMode.Decompress);
    }

    // RFC 1950: method 8 with a window of at most 32 KiB, and a header check that makes the first two bytes,
    // read as a big-endian number, a multiple of 31.
    private static bool HasZlibHeader(byte[] body) =>
        body.Length >= ZlibHeaderLength
        && (body[0] & ZlibMethodMask) == ZlibDeflateMethod
        && body[0] >> ZlibWindowShift <= ZlibMaxWindow
        && ((body[0] << 8) | body[1]) % ZlibHeaderCheckDivisor == 0;

    // RFC 9110 asks a recipient to treat x-gzip as gzip.
    private static bool IsGzip(string coding) =>
        string.Equals(coding, GzipCoding, StringComparison.OrdinalIgnoreCase)
        || string.Equals(coding, LegacyGzipCoding, StringComparison.OrdinalIgnoreCase);

    private static bool IsDeflate(string coding) =>
        string.Equals(coding, DeflateCoding, StringComparison.OrdinalIgnoreCase);
}
