using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DotNetHttpFile.Templates;

internal sealed partial record Template(string Raw, IReadOnlyList<TemplateSegment> Segments)
{
    private const string ExpressionStart = "{{";
    private const string ExpressionEnd = "}}";
    private const char DynamicPrefix = '$';
    private const char Quote = '"';
    private const char CarriageReturn = '\r';
    private const char LineFeed = '\n';
    private const string RequestSource = "request";
    private const string BodyPart = "body";

    private static readonly CompositeFormat s_invalidExpressionFormat =
        CompositeFormat.Parse(Resources.InvalidExpression);

    public static Template Empty { get; } = new(string.Empty, []);

    /// <summary>Splits <paramref name="text"/> into literal text and <c>{{...}}</c> expressions.</summary>
    /// <exception cref="HttpFileException">A closed expression is not a variable, function or reference.</exception>
    public static Template Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<TemplateSegment> segments = [];
        int literalStart = 0;
        int searchFrom = 0;

        while (true)
        {
            int start = text.IndexOf(ExpressionStart, searchFrom, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            int lineEnd = text.AsSpan(start).IndexOfAny(CarriageReturn, LineFeed);
            lineEnd = lineEnd < 0 ? text.Length : start + lineEnd;

            int contentStart = start + ExpressionStart.Length;
            int end = text.IndexOf(ExpressionEnd, contentStart, lineEnd - contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                searchFrom = lineEnd;
                continue;
            }

            // "{{a {{b}}" is the text "{{a " followed by the expression "b".
            start += text.AsSpan(start, end - start).LastIndexOf(ExpressionStart, StringComparison.Ordinal);

            if (start > literalStart)
            {
                segments.Add(new LiteralSegment(text[literalStart..start]));
            }

            segments.Add(ParseExpression(text[(start + ExpressionStart.Length)..end].Trim(), start));
            literalStart = end + ExpressionEnd.Length;
            searchFrom = literalStart;
        }

        if (literalStart < text.Length)
        {
            segments.Add(new LiteralSegment(text[literalStart..]));
        }

        return new Template(text, segments);
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
        new(string.Format(CultureInfo.CurrentCulture, s_invalidExpressionFormat, expression))
        {
            ExpressionOffset = offset,
        };
}
