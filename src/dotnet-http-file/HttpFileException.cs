namespace DotNetHttpFile;

internal sealed class HttpFileException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>
    /// The offset of the failing <c>{{</c> in the parsed template text, when a template expression caused the error.
    /// </summary>
    public int? ExpressionOffset { get; init; }
}
