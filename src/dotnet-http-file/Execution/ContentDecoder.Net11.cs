using System.Buffers;
using System.IO.Compression;

namespace DotNetHttpFile.Execution;

// The decoders are used, and not the streams over them, because a stream ends a body that was cut short
// without a word, as if the part that arrived were the whole of it.
internal static partial class ContentDecoder
{
    private static partial IChunkDecoder CreateGzipDecoder(byte[] body)
    {
        GZipDecoder gzipDecoder = new();

        return new SpanChunkDecoder(body, gzipDecoder, gzipDecoder.Decompress, gzipDecoder.Reset);
    }

    private static partial IChunkDecoder CreateZlibDecoder(byte[] body)
    {
        ZLibDecoder zlibDecoder = new();

        return new SpanChunkDecoder(body, zlibDecoder, zlibDecoder.Decompress, startNextMember: null);
    }

    private static partial IChunkDecoder CreateRawDeflateDecoder(byte[] body)
    {
        DeflateDecoder deflateDecoder = new();

        return new SpanChunkDecoder(body, deflateDecoder, deflateDecoder.Decompress, startNextMember: null);
    }

    /// <summary>
    /// One step of decoding, which the gzip, zlib and deflate decoders have in common.
    /// </summary>
    private delegate OperationStatus Decompress(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        out int bytesConsumed,
        out int bytesWritten);

    private sealed class SpanChunkDecoder(
        byte[] body,
        IDisposable decoder,
        Decompress decompress,
        Action? startNextMember) : IChunkDecoder
    {
        private int _consumed;

        public OperationStatus DecodeChunk(byte[] destination, out int bytesWritten)
        {
            OperationStatus status = decompress(
                body.AsSpan(_consumed),
                destination,
                out int consumed,
                out bytesWritten);
            _consumed += consumed;
            if (status == OperationStatus.Done
                && startNextMember is not null
                && body.AsSpan(_consumed).StartsWith(s_gzipMemberStart))
            {
                startNextMember();

                return OperationStatus.DestinationTooSmall;
            }

            return status;
        }

        public void Dispose() => decoder.Dispose();
    }
}
