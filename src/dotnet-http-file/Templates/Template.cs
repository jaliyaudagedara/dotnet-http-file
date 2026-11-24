using System.Text;
using System.Text.RegularExpressions;

namespace DotNetHttpFile.Templates;

internal sealed partial record Template
{
    public const string ExpressionStart = "{{";
    public const string ExpressionEnd = "}}";

    private const char DynamicPrefix = '$';
    private const char EscapeCharacter = '\\';
    private const char Quote = '"';
    private const char CarriageReturn = '\r';
    private const char LineFeed = '\n';
    private const string RequestSource = "request";
    private const string BodyPart = "body";

    // Private, and without init accessors, so that only Parse can pair segments with their offsets.
    private Template(string raw, IReadOnlyList<TemplateSegment> segments, IReadOnlyList<int> segmentOffsets)
    {
        Raw = raw;
        Segments = segments;
        SegmentOffsets = segmentOffsets;
    }

    public static Template Empty { get; } = new(string.Empty, [], []);

    /// <summary>
    /// The text as written, escapes included.
    /// </summary>
    public string Raw { get; }

    public IReadOnlyList<TemplateSegment> Segments { get; }

    /// <summary>
    /// The offset in <see cref="Raw"/> where each segment starts, parallel to <see cref="Segments"/>. A literal
    /// can be shorter than its raw text, since an escape drops its backslash.
    /// </summary>
    public IReadOnlyList<int> SegmentOffsets { get; }

    /// <summary>
    /// Splits <paramref name="text"/> into literal text and <c>{{...}}</c> expressions. <c>\{{</c> is the
    /// literal text <c>{{</c> and starts no expression.
    /// </summary>
    /// <exception cref="HttpFileException">A closed expression is not a variable, function or reference.</exception>
    public static Template Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<TemplateSegment> segments = [];
        List<int> offsets = [];
        StringBuilder literal = new();
        int literalStart = 0;
        int copiedTo = 0;
        int searchFrom = 0;

        // An opening inside a span already searched closes where the span's first opening does, or not at all
        // when it is before the end of a line with no "}}" left on it. Reusing those, and the end of the
        // current line, keeps the scan linear.
        int unclosedUntil = -1;
        int knownEnd = -1;
        int lineEnd = -1;

        while (true)
        {
            int start = text.IndexOf(ExpressionStart, searchFrom, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            if (IsEscaped(text, start))
            {
                literal.Append(text, copiedTo, start - 1 - copiedTo).Append(ExpressionStart);
                copiedTo = start + ExpressionStart.Length;
                searchFrom = copiedTo;
                continue;
            }

            if (start < unclosedUntil)
            {
                searchFrom = start + 1;
                continue;
            }

            int contentStart = start + ExpressionStart.Length;
            int end = knownEnd;
            if (start >= knownEnd)
            {
                if (start >= lineEnd)
                {
                    lineEnd = text.AsSpan(start).IndexOfAny(CarriageReturn, LineFeed);
                    lineEnd = lineEnd < 0 ? text.Length : start + lineEnd;
                }

                end = text.IndexOf(ExpressionEnd, contentStart, lineEnd - contentStart, StringComparison.Ordinal);
                if (end < 0)
                {
                    unclosedUntil = lineEnd;
                    searchFrom = start + 1;
                    continue;
                }

                knownEnd = end;
            }

            // "{{a {{b}}" is the text "{{a " followed by the expression "b", and in "{{a \{{b}}" neither
            // opening starts an expression.
            int innerStart = text.IndexOf(ExpressionStart, start + 1, end - start - 1, StringComparison.Ordinal);
            if (innerStart >= 0)
            {
                searchFrom = innerStart;
                continue;
            }

            literal.Append(text, copiedTo, start - copiedTo);
            if (literal.Length > 0)
            {
                segments.Add(new LiteralSegment(literal.ToString()));
                offsets.Add(literalStart);
                literal.Clear();
            }

            segments.Add(ParseExpression(text[contentStart..end].Trim(), start));
            offsets.Add(start);
            literalStart = end + ExpressionEnd.Length;
            copiedTo = literalStart;
            searchFrom = literalStart;
        }

        literal.Append(text, copiedTo, text.Length - copiedTo);
        if (literal.Length > 0)
        {
            segments.Add(new LiteralSegment(literal.ToString()));
            offsets.Add(literalStart);
        }

        return new Template(text, segments, offsets);
    }

    /// <summary>
    /// Gets whether the <c>{{</c> at <paramref name="start"/> in <paramref name="text"/> is escaped.
    /// </summary>
    public static bool IsEscaped(string text, int start)
    {
        ArgumentNullException.ThrowIfNull(text);

        return start > 0 && text[start - 1] == EscapeCharacter;
    }

    [GeneratedRegex(
        @"^(?<name>[^.\s]+)\.(?<source>request|response)\.(?<part>body|headers)\.(?<selector>.+)$",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();

    private static TemplateSegment ParseExpression(string expression, int offset)
    {
        if (expression.StartsWith(DynamicPrefix))
        {
            return ParseDynamic(expression, offset);
        }

        Match match = ReferencePattern().Match(expression);
        if (match.Success)
        {
            return new RequestReferenceSegment(
                match.Groups["name"].Value,
                match.Groups["source"].Value == RequestSource ? ReferenceSource.Request : ReferenceSource.Response,
                match.Groups["part"].Value == BodyPart ? ReferencePart.Body : ReferencePart.Headers,
                match.Groups["selector"].Value,
                expression);
        }

        if (expression.Length == 0 || expression.Any(char.IsWhiteSpace))
        {
            throw InvalidExpression(expression, offset);
        }

        return new VariableSegment(expression);
    }

    private static DynamicSegment ParseDynamic(string expression, int offset)
    {
        int position = 1;
        while (position < expression.Length && !char.IsWhiteSpace(expression[position]))
        {
            position++;
        }

        string function = expression[1..position];
        if (function.Length == 0)
        {
            throw InvalidExpression(expression, offset);
        }

        List<string> arguments = [];
        while (true)
        {
            while (position < expression.Length && char.IsWhiteSpace(expression[position]))
            {
                position++;
            }

            if (position == expression.Length)
            {
                break;
            }

            int end;
            if (expression[position] == Quote)
            {
                end = expression.IndexOf(Quote, position + 1);
                if (end < 0)
                {
                    throw InvalidExpression(expression, offset);
                }

                arguments.Add(expression[(position + 1)..end]);
                position = end + 1;

                if (position < expression.Length && !char.IsWhiteSpace(expression[position]))
                {
                    throw InvalidExpression(expression, offset);
                }
            }
            else
            {
                end = position;
                while (end < expression.Length && !char.IsWhiteSpace(expression[end]))
                {
                    if (expression[end] == Quote)
                    {
                        throw InvalidExpression(expression, offset);
                    }

                    end++;
                }

                arguments.Add(expression[position..end]);
                position = end;
            }
        }

        return new DynamicSegment(function, arguments);
    }

    private static HttpFileException InvalidExpression(string expression, int offset) =>
        new(Messages.Format(Resources.InvalidExpression, expression))
        {
            ExpressionOffset = offset,
        };
}
