namespace DotNetHttpFile;

internal sealed class HttpFileException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>
    /// The offset of the failing <c>{{</c> in the parsed template text, when a template expression caused the error.
    /// </summary>
    public int? ExpressionOffset { get; init; }

    /// <summary>
    /// The exit code of a run this exception ends. <see cref="ExitCodes.NotSent"/> unless a response arrived.
    /// </summary>
    public int ExitCode { get; init; } = ExitCodes.NotSent;
}
