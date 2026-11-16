using System.Text;

namespace DotNetHttpFile;

/// <summary>
/// Turns a failed read of a file into a user-facing error.
/// </summary>
internal static class FileReadFailure
{
    private static readonly CompositeFormat s_fileReadFailedFormat = CompositeFormat.Parse(Resources.FileReadFailed);

    /// <summary>
    /// Whether <paramref name="exception"/> is what a file read throws when the file cannot be read.
    /// </summary>
    public static bool IsFileReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    /// <summary>
    /// Creates the error for <paramref name="exception"/>, whose message names the path and the operating
    /// system's reason, and never the content of the file.
    /// </summary>
    public static HttpFileException Create(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new HttpFileException(Messages.Format(s_fileReadFailedFormat, exception.Message), exception);
    }
}
