using System.Buffers;

namespace DotNetHttpFile.Execution;

/// <summary>
/// Decodes one body for <see cref="ContentDecoder"/>, a chunk at a time.
/// </summary>
internal interface IChunkDecoder : IDisposable
{
    /// <summary>
    /// Decodes the next chunk of the body into <paramref name="destination"/>.
    /// </summary>
    /// <returns>
    /// <see cref="OperationStatus.Done"/> when the data has ended, <see cref="OperationStatus.DestinationTooSmall"/>
    /// when more of it is to come, and any other status when the data is not valid or the body ended before it
    /// did.
    /// </returns>
    OperationStatus DecodeChunk(byte[] destination, out int bytesWritten);
}
