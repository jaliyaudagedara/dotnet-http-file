using System.Globalization;

namespace DotNetHttpFile;

/// <summary>
/// Builds user-facing messages from the format strings in <see cref="Resources"/>.
/// </summary>
internal static class Messages
{
    /// <summary>
    /// Separates the items of a list in a message, such as the names a user can choose from.
    /// </summary>
    public const string ListSeparator = ", ";

    /// <summary>
    /// Formats a message in the current culture.
    /// </summary>
    /// <param name="format">A composite format string from <see cref="Resources"/>.</param>
    /// <param name="arguments">The values for the format items.</param>
    /// <returns>
    /// The formatted message.
    /// </returns>
    public static string Format(string format, params ReadOnlySpan<object?> arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);
}
