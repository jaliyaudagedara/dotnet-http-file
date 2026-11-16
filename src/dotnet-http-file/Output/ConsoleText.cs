using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotNetHttpFile.Output;

/// <summary>
/// Writes text to a console safely: control characters made visible, and the line breaks the text has and no
/// others.
/// </summary>
internal static class ConsoleText
{
    private const string ControlCharacterPrefix = @"\x";
    private const string ControlCharacterFormat = "X2";
    private const int HexDigitCount = 2;

    /// <summary>
    /// Replaces every C0 and C1 control character and DEL with its code as text, so that what a server sent
    /// or a file holds cannot move the cursor, clear the screen or set the window title. A tab and a line break
    /// are kept; a carriage return that no line feed follows is replaced, because it would overwrite the line.
    /// </summary>
    /// <param name="text">The text to show.</param>
    /// <param name="keepLineBreaks">False to replace line breaks too, so the text stays on one line.</param>
    public static string WithVisibleControlCharacters(string text, bool keepLineBreaks = true)
    {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder? builder = null;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            bool isKept = !char.IsControl(character)
                || character == '\t'
                || (keepLineBreaks && character == '\n')
                || (keepLineBreaks && character == '\r' && index + 1 < text.Length && text[index + 1] == '\n');

            if (isKept)
            {
                builder?.Append(character);
                continue;
            }

            builder ??= new StringBuilder(text, 0, index, text.Length + ControlCharacterPrefix.Length + HexDigitCount);
            builder.Append(ControlCharacterPrefix)
                .Append(((int)character).ToString(ControlCharacterFormat, CultureInfo.InvariantCulture));
        }

        return builder?.ToString() ?? text;
    }

    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="target"/> with the line breaks it has and no others, its
    /// control characters made visible.
    /// </summary>
    public static void WriteLines(IAnsiConsole target, string text, Style? style = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.Write(new UnwrappedLines(WithVisibleControlCharacters(text), style));
    }

    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="target"/> as exactly one line, every control character
    /// made visible, line breaks included.
    /// </summary>
    public static void WriteLine(IAnsiConsole target, string text, Style? style = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.Write(new UnwrappedLines(WithVisibleControlCharacters(text, keepLineBreaks: false), style));
    }

    /// <summary>
    /// Text written with the line breaks it has and no others. Spectre's <see cref="Text"/> wraps at the console
    /// width, which would break a header value or a body line that a piped capture must keep whole.
    /// </summary>
    private sealed class UnwrappedLines(string text, Style? style) : IRenderable
    {
        private readonly string[] _lines = text.Split('\n');

        public Measurement Measure(RenderOptions options, int maxWidth)
        {
            int width = _lines.Max(line => new Segment(line).CellCount());

            return new Measurement(width, width);
        }

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            foreach (string line in _lines)
            {
                string content = line.EndsWith('\r') ? line[..^1] : line;
                if (content.Length > 0)
                {
                    yield return style is null ? new Segment(content) : new Segment(content, style.Value);
                }

                yield return Segment.LineBreak;
            }
        }
    }
}
