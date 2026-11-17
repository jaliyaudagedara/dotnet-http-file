namespace DotNetHttpFile.Output;

/// <summary>
/// What a run prints.
/// </summary>
internal enum OutputMode
{
    /// <summary>
    /// The request, the status line, the response headers and a readable body.
    /// </summary>
    Full,

    /// <summary>
    /// The response body alone, byte for byte.
    /// </summary>
    Body,

    /// <summary>
    /// The status line and the response headers.
    /// </summary>
    Headers,

    /// <summary>
    /// The status line alone.
    /// </summary>
    Status,
}
