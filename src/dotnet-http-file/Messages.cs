using System.Globalization;
using System.Text;

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

    public static string Format(CompositeFormat format, params ReadOnlySpan<object?> arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);
}
