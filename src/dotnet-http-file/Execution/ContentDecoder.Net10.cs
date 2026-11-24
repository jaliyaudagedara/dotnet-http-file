using System.Buffers;
using System.Buffers.Binary;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace DotNetHttpFile.Execution;

// .NET 10 has no span decoders, and its streams end a body that was cut short without a word, as if the part
// that arrived were the whole of it. SharpZipLib's Inflater reports it. The gzip framing is read here because
// SharpZipLib's gzip stream takes a corrupt header after the first member as the end of the body.
internal static partial class ContentDecoder
{
    // RFC 1952 member framing.
    private const int GzipFixedHeaderLength = 10;
    private const int GzipMethodOffset = 2;
    private const int GzipFlagsOffset = 3;
    private const byte GzipHeaderCrcFlag = 0x02;
    private const byte GzipExtraFlag = 0x04;
    private const byte GzipNameFlag = 0x08;
    private const byte GzipCommentFlag = 0x10;
    private const byte GzipReservedFlags = 0xE0;
    private const int GzipExtraLengthLength = 2;
    private const int GzipHeaderCrcLength = 2;
    private const int GzipTrailerLength = 8;
    private const int GzipSizeOffset = 4;

    private static partial IChunkDecoder CreateGzipDecoder(byte[] body) =>
        new InflaterChunkDecoder(body, InflaterFraming.Gzip);

    private static partial IChunkDecoder CreateZlibDecoder(byte[] body) =>
        new InflaterChunkDecoder(body, InflaterFraming.Zlib);

    private static partial IChunkDecoder CreateRawDeflateDecoder(byte[] body) =>
        new InflaterChunkDecoder(body, InflaterFraming.RawDeflate);

    // InflaterHuffmanTree indexes past its table on some malformed dynamic Huffman headers.
    private static bool IsSharpZipLibFailure(Exception exception) =>
        exception is SharpZipBaseException or IndexOutOfRangeException;

    // RFC 1952: the member header, with its optional fields. A header cut short is not valid.
    private static bool TryReadGzipHeader(byte[] body, int memberStart, out int headerLength)
    {
        headerLength = 0;
        ReadOnlySpan<byte> member = body.AsSpan(memberStart);
        if (member.Length < GzipFixedHeaderLength
            || !member.StartsWith(s_gzipMemberStart)
            || member[GzipMethodOffset] != DeflateMethod
            || (member[GzipFlagsOffset] & GzipReservedFlags) != 0)
        {
            return false;
        }

        byte flags = member[GzipFlagsOffset];
        int length = GzipFixedHeaderLength;
        if ((flags & GzipExtraFlag) != 0)
        {
            if (member.Length < length + GzipExtraLengthLength)
            {
                return false;
            }

            length += GzipExtraLengthLength + BinaryPrimitives.ReadUInt16LittleEndian(member[length..]);
        }

        if (((flags & GzipNameFlag) != 0 && !TrySkipZeroTerminated(member, ref length))
            || ((flags & GzipCommentFlag) != 0 && !TrySkipZeroTerminated(member, ref length)))
        {
            return false;
        }

        if ((flags & GzipHeaderCrcFlag) != 0)
        {
            if (member.Length < length + GzipHeaderCrcLength)
            {
                return false;
            }

            // The header CRC is the low two bytes of the CRC-32 of the header before it.
            Crc32 headerCrc = new();
            headerCrc.Update(new ArraySegment<byte>(body, memberStart, length));
            if ((ushort)headerCrc.Value != BinaryPrimitives.ReadUInt16LittleEndian(member[length..]))
            {
                return false;
            }

            length += GzipHeaderCrcLength;
        }

        if (length > member.Length)
        {
            return false;
        }

        headerLength = length;

        return true;
    }

    private static bool TrySkipZeroTerminated(ReadOnlySpan<byte> member, ref int offset)
    {
        if (offset > member.Length)
        {
            return false;
        }

        int terminator = member[offset..].IndexOf((byte)0);
        if (terminator < 0)
        {
            return false;
        }

        offset += terminator + 1;

        return true;
    }

    private enum InflaterFraming
    {
        Gzip,
        Zlib,
        RawDeflate,
    }

    private sealed class InflaterChunkDecoder : IChunkDecoder
    {
        private readonly byte[] _body;
        private readonly Inflater _inflater;

        // Set for gzip only, whose members are framed here rather than by the inflater.
        private readonly Crc32? _memberCrc;
        private int _memberStart;
        private bool _readsMemberHeader;

        public InflaterChunkDecoder(byte[] body, InflaterFraming framing)
        {
            _body = body;
            _inflater = new Inflater(noHeader: framing != InflaterFraming.Zlib);
            if (framing == InflaterFraming.Gzip)
            {
                _memberCrc = new Crc32();
                _readsMemberHeader = true;
            }
            else
            {
                _inflater.SetInput(body);
            }
        }

        public OperationStatus DecodeChunk(byte[] destination, out int bytesWritten)
        {
            bytesWritten = 0;
            if (_readsMemberHeader && !TryStartMember())
            {
                return OperationStatus.InvalidData;
            }

            try
            {
                bytesWritten = _inflater.Inflate(destination);
            }
            catch (Exception exception) when (IsSharpZipLibFailure(exception))
            {
                bytesWritten = 0;

                return OperationStatus.InvalidData;
            }

            _memberCrc?.Update(new ArraySegment<byte>(destination, 0, bytesWritten));
            if (_inflater.IsFinished)
            {
                return _memberCrc is null ? OperationStatus.Done : EndMember(_memberCrc);
            }

            if (bytesWritten > 0)
            {
                return OperationStatus.DestinationTooSmall;
            }

            // A zlib body that asks for a preset dictionary cannot be decoded: HTTP has no way to supply one.
            return _inflater.IsNeedingDictionary ? OperationStatus.InvalidData : OperationStatus.NeedMoreData;
        }

        // Inflater holds only managed buffers.
        public void Dispose()
        {
        }

        private bool TryStartMember()
        {
            if (!TryReadGzipHeader(_body, _memberStart, out int headerLength))
            {
                return false;
            }

            int dataStart = _memberStart + headerLength;
            _inflater.SetInput(_body, dataStart, _body.Length - dataStart);
            _readsMemberHeader = false;

            return true;
        }

        // RFC 1952: the trailer holds the CRC-32 of the member's content and its length modulo 2^32.
        private OperationStatus EndMember(Crc32 memberCrc)
        {
            int trailerStart = _body.Length - _inflater.RemainingInput;
            ReadOnlySpan<byte> trailer = _body.AsSpan(trailerStart);
            uint memberLength = unchecked((uint)_inflater.TotalOut);
            if (trailer.Length < GzipTrailerLength
                || BinaryPrimitives.ReadUInt32LittleEndian(trailer) != (uint)memberCrc.Value
                || BinaryPrimitives.ReadUInt32LittleEndian(trailer[GzipSizeOffset..]) != memberLength)
            {
                return OperationStatus.InvalidData;
            }

            _memberStart = trailerStart + GzipTrailerLength;
            if (!_body.AsSpan(_memberStart).StartsWith(s_gzipMemberStart))
            {
                return OperationStatus.Done;
            }

            _inflater.Reset();
            memberCrc.Reset();
            _readsMemberHeader = true;

            return OperationStatus.DestinationTooSmall;
        }
    }
}
