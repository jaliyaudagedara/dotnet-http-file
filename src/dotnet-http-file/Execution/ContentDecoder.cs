using System.Buffers;

namespace DotNetHttpFile.Execution;

/// <summary>
/// Decodes a response body from the content codings the tool supports, gzip and deflate.
/// </summary>
/// <remarks>
/// The decoding itself is runtime-specific and lives in the other parts of this class.
/// </remarks>
internal static partial class ContentDecoder
{
    /// <summary>
    /// The most bytes a body may decode to, 100 MiB.
    /// </summary>
    public const int MaxDecodedLength = MaxDecodedMebibytes * BytesPerMebibyte;

    /// <summary>
    /// <see cref="MaxDecodedLength"/> in MiB, for messages.
    /// </summary>
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
    private const int DeflateMethod = 8;
    private const int ZlibWindowShift = 4;
    private const int ZlibMaxWindow = 7;
    private const int ZlibHeaderCheckDivisor = 31;

    // RFC 1952: each member of a gzip body starts with these two bytes.
    private static readonly byte[] s_gzipMemberStart = [0x1F, 0x8B];

    /// <summary>
    /// Whether <paramref name="coding"/> is one the tool decodes.
    /// </summary>
    public static bool IsSupported(string coding)
    {
        ArgumentNullException.ThrowIfNull(coding);

        return IsGzip(coding) || IsDeflate(coding);
    }

    /// <summary>
    /// Decodes <paramref name="body"/> from <paramref name="coding"/>, which must be supported. A deflate body is
    /// read as zlib-wrapped when it starts with a zlib header, and as raw deflate otherwise. A gzip body may hold
    /// several members, one after the other. An empty body decodes to nothing, and bytes after the end of the data
    /// are ignored.
    /// </summary>
    /// <returns>
    /// The decoded bytes, or null when they would be more than <paramref name="maxDecodedLength"/>. Decoding stops
    /// at the limit.
    /// </returns>
    /// <exception cref="InvalidDataException">
    /// <paramref name="body"/> is not valid data in the coding, or ends before the data does.
    /// </exception>
    public static byte[]? Decode(
        byte[] body,
        string coding,
        int maxDecodedLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(coding);

        if (!IsGzip(coding) && !IsDeflate(coding))
        {
            throw new ArgumentOutOfRangeException(nameof(coding), coding, null);
        }

        // A body inside another coding can decode to nothing, which the decoders would take as cut short.
        if (body.Length == 0)
        {
            return [];
        }

        using IChunkDecoder decoder = IsGzip(coding) ? CreateGzipDecoder(body)
            : HasZlibHeader(body) ? CreateZlibDecoder(body)
            : CreateRawDeflateDecoder(body);

        return Decode(decoder, body.Length, maxDecodedLength, cancellationToken);
    }

    private static partial IChunkDecoder CreateGzipDecoder(byte[] body);

    private static partial IChunkDecoder CreateZlibDecoder(byte[] body);

    private static partial IChunkDecoder CreateRawDeflateDecoder(byte[] body);

    private static byte[]? Decode(
        IChunkDecoder decoder,
        int bodyLength,
        int maxDecodedLength,
        CancellationToken cancellationToken)
    {
        using MemoryStream output = new((int)Math.Min((long)bodyLength * ExpectedExpansion, maxDecodedLength));
        byte[] buffer = new byte[BufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OperationStatus status = decoder.DecodeChunk(buffer, out int written);
            if (output.Length + written > maxDecodedLength)
            {
                return null;
            }

            output.Write(buffer, 0, written);
            switch (status)
            {
                case OperationStatus.DestinationTooSmall:
                    break;

                case OperationStatus.Done:
                    return output.ToArray();

                default:
                    // The data is not valid, or the body ended before the data did.
                    throw new InvalidDataException();
            }
        }
    }

    // RFC 1950: method 8 with a window of at most 32 KiB, and a header check that makes the first two bytes,
    // read as a big-endian number, a multiple of 31.
    private static bool HasZlibHeader(byte[] body) =>
        body.Length >= ZlibHeaderLength
        && (body[0] & ZlibMethodMask) == DeflateMethod
        && body[0] >> ZlibWindowShift <= ZlibMaxWindow
        && ((body[0] << 8) | body[1]) % ZlibHeaderCheckDivisor == 0;

    // RFC 9110 asks a recipient to treat x-gzip as gzip.
    private static bool IsGzip(string coding) =>
        string.Equals(coding, GzipCoding, StringComparison.OrdinalIgnoreCase)
        || string.Equals(coding, LegacyGzipCoding, StringComparison.OrdinalIgnoreCase);

    private static bool IsDeflate(string coding) =>
        string.Equals(coding, DeflateCoding, StringComparison.OrdinalIgnoreCase);
}
