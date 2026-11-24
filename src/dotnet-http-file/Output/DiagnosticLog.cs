using DotNetHttpFile.Resolution;
using Spectre.Console;

namespace DotNetHttpFile.Output;

/// <summary>
/// The diagnostic lines of <c>--verbose</c>. They say what the tool does with the request, so they go through the
/// secret mask, and they go to standard error, so that standard output holds only what <c>--show</c> asks for.
/// </summary>
/// <param name="error">The console over standard error.</param>
/// <param name="mask">The provider-resolved values of the run, or null to write the lines unmasked.</param>
internal sealed class DiagnosticLog(IAnsiConsole error, SecretMask? mask = null)
{
    private static readonly Style s_style = new(foreground: Color.Grey);

    /// <summary>
    /// Whether <see cref="Write"/> writes anything.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Writes <paramref name="message"/> to standard error as a <c>verbose:</c> line, masked and with its control
    /// characters made visible. Does nothing unless <see cref="IsEnabled"/>.
    /// </summary>
    public void Write(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!IsEnabled)
        {
            return;
        }

        string line = Messages.Format(Resources.OutputVerbose, message);

        // Masked before the line breaks are made visible, so a secret that holds one still matches.
        ConsoleText.WriteLine(error, mask?.Apply(line) ?? line, s_style);
    }
}
