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
    /// <summary>
    /// The text between a header name and its value.
    /// </summary>
    public const string HeaderSeparator = ": ";

    private const string ControlCharacterPrefix = @"\x";
    private const string ControlCharacterFormat = "X2";
    private const int HexDigitCount = 2;
    private const string BidiControlPrefix = @"\u";
    private const string BidiControlFormat = "X4";

    /// <summary>
    /// Replaces every C0 and C1 control character and DEL with its code as text, so that what a server sent
    /// or a file holds cannot move the cursor, clear the screen or set the window title. A tab and a line break
    /// are kept; a carriage return that no line feed follows is replaced, because it would overwrite the line.
    /// A bidirectional embedding, override or isolate (U+202A to U+202E, U+2066 to U+2069) is replaced with its
    /// <c>\uXXXX</c> code too, because it would change the order in which the terminal shows the text around it.
    /// A directional mark (U+200E, U+200F, U+061C) is kept: it cannot reorder text and is ordinary in Arabic and
    /// Hebrew.
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
            bool isKept = !IsBidiControl(character)
                && (!char.IsControl(character)
                    || character == '\t'
                    || (keepLineBreaks && character == '\n')
                    || (keepLineBreaks && character == '\r' && index + 1 < text.Length && text[index + 1] == '\n'));

            if (isKept)
            {
                builder?.Append(character);
                continue;
            }

            builder ??= new StringBuilder(text, 0, index, text.Length + ControlCharacterPrefix.Length + HexDigitCount);
            builder.Append(Code(character));
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

        target.Write(new UnwrappedLines(WithVisibleControlCharacters(text), StyleFor(target, style)));
    }

    /// <summary>
    /// Writes <paramref name="header"/>, a header name and value joined by <see cref="HeaderSeparator"/>, like
    /// <see cref="WriteLines"/>, with the name in <paramref name="nameStyle"/>.
    /// </summary>
    /// <remarks>
    /// The name is found in the text as written, after its control characters are made visible, so a caller that
    /// masks the line first can move the styling but never what is shown. A line without the separator before
    /// its first line break is written unstyled.
    /// </remarks>
    public static void WriteHeader(IAnsiConsole target, string header, Style nameStyle)
    {
        ArgumentNullException.ThrowIfNull(target);

        string text = WithVisibleControlCharacters(header);
        int separator = text.IndexOf(HeaderSeparator, StringComparison.Ordinal);
        int lineBreak = text.IndexOf('\n');
        bool isNameStyled = separator > 0 && (lineBreak < 0 || separator < lineBreak);

        target.Write(new UnwrappedLines(text, style: null)
        {
            PrefixLength = isNameStyled ? separator : 0,
            PrefixStyle = StyleFor(target, nameStyle),
        });
    }

    /// <summary>
    /// Writes <paramref name="text"/> to <paramref name="target"/> as exactly one line, every control character
    /// made visible, line breaks included.
    /// </summary>
    public static void WriteLine(IAnsiConsole target, string text, Style? style = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.Write(new UnwrappedLines(
            WithVisibleControlCharacters(text, keepLineBreaks: false),
            StyleFor(target, style)));
    }

    private static string Code(char character) =>
        IsBidiControl(character)
            ? BidiControlPrefix + ((int)character).ToString(BidiControlFormat, CultureInfo.InvariantCulture)
            : ControlCharacterPrefix + ((int)character).ToString(ControlCharacterFormat, CultureInfo.InvariantCulture);

    private static bool IsBidiControl(char character) =>
        character is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');

    // Spectre still writes a decoration such as bold when the console has no colour (NO_COLOR).
    private static Style? StyleFor(IAnsiConsole target, Style? style) =>
        target.Profile.Capabilities.ColorSystem == ColorSystem.NoColors ? null : style;

    /// <summary>
    /// Text written with the line breaks it has and no others. Spectre's <see cref="Text"/> wraps at the console
    /// width, which would break a header value or a body line that a piped capture must keep whole.
    /// </summary>
    private sealed class UnwrappedLines(string text, Style? style) : IRenderable
    {
        private readonly string[] _lines = text.Split('\n');

        /// <summary>
        /// Gets the number of characters at the start of the first line written in <see cref="PrefixStyle"/>.
        /// </summary>
        public int PrefixLength { get; init; }

        /// <summary>
        /// Gets the style of the first <see cref="PrefixLength"/> characters.
        /// </summary>
        public Style? PrefixStyle { get; init; }

        public Measurement Measure(RenderOptions options, int maxWidth)
        {
            int width = _lines.Max(line => new Segment(line).CellCount());

            return new Measurement(width, width);
        }

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            bool isFirstLine = true;
            foreach (string line in _lines)
            {
                string content = line.EndsWith('\r') ? line[..^1] : line;
                if (isFirstLine && PrefixStyle is Style prefixStyle && PrefixLength > 0)
                {
                    yield return new Segment(content[..PrefixLength], prefixStyle);
                    content = content[PrefixLength..];
                }

                isFirstLine = false;
                if (content.Length > 0)
                {
                    yield return style is null ? new Segment(content) : new Segment(content, style.Value);
                }

                yield return Segment.LineBreak;
            }
        }
    }
}
