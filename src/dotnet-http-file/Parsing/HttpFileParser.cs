using System.Text;
using System.Text.RegularExpressions;
using DotNetHttpFile.Templates;

namespace DotNetHttpFile.Parsing;

internal static partial class HttpFileParser
{
    private const char ByteOrderMark = '\uFEFF';
    private const string Separator = "###";
    private const char HashComment = '#';
    private const string SlashComment = "//";
    private const char VariablePrefix = '@';
    private const char VariableAssignment = '=';
    private const char HeaderDelimiter = ':';
    private const string DefaultMethod = "GET";
    private const char Period = '.';
    private const int MaxEchoedLength = 32;
    private const string Ellipsis = "...";

    private static readonly HashSet<string> s_methods =
    [
        with(StringComparer.Ordinal),
        "OPTIONS",
        DefaultMethod,
        "HEAD",
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
        "TRACE",
        "CONNECT",
    ];

    private static readonly CompositeFormat s_duplicateRequestNameFormat =
        CompositeFormat.Parse(Resources.DuplicateRequestName);

    private static readonly CompositeFormat s_invalidHeaderFormat =
        CompositeFormat.Parse(Resources.InvalidHeader);

    private static readonly CompositeFormat s_invalidRequestNameFormat =
        CompositeFormat.Parse(Resources.InvalidRequestName);

    private static readonly CompositeFormat s_invalidVariableDefinitionFormat =
        CompositeFormat.Parse(Resources.InvalidVariableDefinition);

    private static readonly CompositeFormat s_messageAtLineFormat =
        CompositeFormat.Parse(Resources.MessageAtLine);

    private static readonly CompositeFormat s_unknownHttpMethodFormat =
        CompositeFormat.Parse(Resources.UnknownHttpMethod);

    /// <summary>
    /// Parses the text of an <c>.http</c> file into its variables and request definitions.
    /// </summary>
    /// <exception cref="HttpFileException">The content is not a valid <c>.http</c> file.</exception>
    public static HttpFile Parse(string path, string content)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(content);

        if (content.StartsWith(ByteOrderMark))
        {
            content = content[1..];
        }

        List<FileVariable> variables = [];
        List<RequestDefinition> requests = [];
        RequestBuilder? current = null;
        string? pendingName = null;
        string? pendingTitle = null;

        foreach (SourceLine line in SplitLines(content))
        {
            string text = content[line.Start..line.End];

            if (text.StartsWith(Separator, StringComparison.Ordinal))
            {
                AddRequest(requests, current, content);
                current = null;
                pendingName = null;
                pendingTitle = ParseTitle(text);
                continue;
            }

            if (current is { IsInBody: true })
            {
                current.AddBodyLine(line, text);
                continue;
            }

            text = text.Trim();
            if (text.Length == 0)
            {
                current?.IsInBody = true;
                continue;
            }

            if (IsComment(text))
            {
                Match match = NameCommentPattern().Match(text);
                if (current is null && match.Success)
                {
                    pendingName = ParseRequestName(match.Groups["arguments"].Value, line.Number);
                }

                continue;
            }

            if (current is not null)
            {
                current.Headers.Add(ParseHeader(text, line.Number));
            }
            else if (text.StartsWith(VariablePrefix))
            {
                variables.Add(ParseVariable(text, line.Number));
            }
            else
            {
                current = ParseRequestLine(text, pendingName, pendingTitle, line.Number);
            }
        }

        AddRequest(requests, current, content);
        ThrowIfDuplicateNames(requests);

        return new HttpFile(path, variables, requests);
    }

    /// <summary>
    /// Reads and parses the <c>.http</c> file at <paramref name="path"/>.
    /// </summary>
    /// <exception cref="HttpFileException">The content is not a valid <c>.http</c> file.</exception>
    public static async Task<HttpFile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        string content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        return Parse(path, content);
    }

    /// <summary>
    /// Counts the line breaks in <paramref name="text"/>, taking CRLF as one.
    /// </summary>
    public static int CountLineTerminators(ReadOnlySpan<char> text)
    {
        int count = 0;
        for (int index = 0; index < text.Length; index++)
        {
            bool isCarriageReturnLineFeed =
                text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n';
            if (text[index] == '\n' || (text[index] == '\r' && !isCarriageReturnLineFeed))
            {
                count++;
            }
        }

        return count;
    }

    [GeneratedRegex(
        @"^(#|//)\s*@name(?<arguments>\s.*)?$",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)]
    private static partial Regex NameCommentPattern();

    [GeneratedRegex(
        @"^(?<target>.+?)(\s+(?<version>HTTP/\S+))?$",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)]
    private static partial Regex RequestLinePattern();

    private static bool IsComment(string text) =>
        text.StartsWith(HashComment) || text.StartsWith(SlashComment, StringComparison.Ordinal);

    private static List<SourceLine> SplitLines(string content)
    {
        List<SourceLine> lines = [];
        int start = 0;

        while (start < content.Length)
        {
            int length = content.AsSpan(start).IndexOfAny('\r', '\n');
            if (length < 0)
            {
                lines.Add(new SourceLine(lines.Count + 1, start, content.Length));
                break;
            }

            int end = start + length;
            lines.Add(new SourceLine(lines.Count + 1, start, end));

            bool isCarriageReturnLineFeed =
                content[end] == '\r' && end + 1 < content.Length && content[end + 1] == '\n';
            start = end + (isCarriageReturnLineFeed ? 2 : 1);
        }

        return lines;
    }

    private static string ParseRequestName(string arguments, int line)
    {
        string[] names = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (names.Length != 1)
        {
            throw new HttpFileException(Messages.Format(
                s_invalidRequestNameFormat,
                LeadingToken(names.FirstOrDefault(string.Empty)),
                line));
        }

        return names[0];
    }

    private static string? ParseTitle(string separatorLine)
    {
        string title = separatorLine[Separator.Length..].Trim();

        return title.Length == 0 ? null : title;
    }

    private static FileVariable ParseVariable(string text, int line)
    {
        int assignment = text.IndexOf(VariableAssignment);
        string name = assignment < 0 ? string.Empty : text[1..assignment].TrimEnd();
        if (name.Length == 0 || name.Any(char.IsWhiteSpace))
        {
            throw new HttpFileException(Messages.Format(s_invalidVariableDefinitionFormat, LeadingToken(text), line));
        }

        return new FileVariable(name, ParseTemplate(text[(assignment + 1)..].TrimStart(), line), line);
    }

    private static RequestBuilder ParseRequestLine(string text, string? name, string? title, int line)
    {
        Match match = RequestLinePattern().Match(text);
        Group httpVersion = match.Groups["version"];
        string target = match.Groups["target"].Value;
        string method = DefaultMethod;
        string url = target;

        int methodEnd = IndexOfWhiteSpaceOutsideExpressions(target);
        if (methodEnd >= 0)
        {
            method = target[..methodEnd];
            if (!s_methods.Contains(method))
            {
                throw new HttpFileException(Messages.Format(s_unknownHttpMethodFormat, LeadingToken(method), line));
            }

            url = target[methodEnd..].TrimStart();
        }

        return new RequestBuilder(
            name,
            title,
            method,
            ParseTemplate(url, line),
            httpVersion.Success ? httpVersion.Value : null,
            line);
    }

    // Whitespace inside {{...}} belongs to the URL, so "https://host/{{$randomInt 1 9}}" has no method.
    // An expression is what Template.Parse takes as one: a "{{" with no "}}" after it is plain text, and in
    // "{{a {{b}}" only "{{b}}" is an expression, and "\{{a b}}" is plain text.
    private static int IndexOfWhiteSpaceOutsideExpressions(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            ReadOnlySpan<char> remaining = text.AsSpan(index);
            if (remaining.StartsWith(Template.ExpressionStart, StringComparison.Ordinal))
            {
                if (Template.IsEscaped(text, index))
                {
                    index += Template.ExpressionStart.Length - 1;
                    continue;
                }

                int end = remaining.IndexOf(Template.ExpressionEnd, StringComparison.Ordinal);
                if (end >= 0 && remaining[1..end].IndexOf(Template.ExpressionStart, StringComparison.Ordinal) < 0)
                {
                    index += end + Template.ExpressionEnd.Length - 1;
                }
            }
            else if (char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static HeaderDefinition ParseHeader(string text, int line)
    {
        int delimiter = text.IndexOf(HeaderDelimiter);
        string name = delimiter < 0 ? string.Empty : text[..delimiter].TrimEnd();
        if (name.Length == 0 || name.Any(char.IsWhiteSpace))
        {
            throw new HttpFileException(Messages.Format(s_invalidHeaderFormat, LeadingToken(text), line));
        }

        return new HeaderDefinition(name, ParseTemplate(text[(delimiter + 1)..].TrimStart(), line), line);
    }

    // The rest of a malformed line can hold a secret, and so can a long token that is the whole line, so errors
    // echo only the start of the line and only so much of it.
    private static string LeadingToken(string text)
    {
        int end = 0;
        while (end < text.Length
            && !char.IsWhiteSpace(text[end])
            && text[end] is not (HeaderDelimiter or VariableAssignment))
        {
            end++;
        }

        return end > MaxEchoedLength ? text[..MaxEchoedLength] + Ellipsis : text[..end];
    }

    private static Template ParseTemplate(string text, int line)
    {
        try
        {
            return Template.Parse(text);
        }
        catch (HttpFileException exception)
        {
            int expressionLine = line + CountLineTerminators(text.AsSpan(0, exception.ExpressionOffset ?? 0));
            string message = exception.Message.EndsWith(Period) ? exception.Message[..^1] : exception.Message;

            throw new HttpFileException(Messages.Format(s_messageAtLineFormat, message, expressionLine), exception);
        }
    }

    private static void AddRequest(List<RequestDefinition> requests, RequestBuilder? builder, string content)
    {
        if (builder is null)
        {
            return;
        }

        Template? body = null;
        int? bodyLine = null;
        if (builder is { HasBodyText: true, FirstBodyLine: SourceLine first, LastBodyLine: SourceLine last })
        {
            body = ParseTemplate(content[first.Start..last.End], first.Number);
            bodyLine = first.Number;
        }

        requests.Add(new RequestDefinition(builder.Method, builder.Url, builder.Headers, builder.Line)
        {
            Name = builder.Name,
            Title = builder.Title,
            HttpVersion = builder.HttpVersion,
            Body = body,
            BodyLine = bodyLine,
        });
    }

    private static void ThrowIfDuplicateNames(List<RequestDefinition> requests)
    {
        Dictionary<string, int> linesByName = [with(StringComparer.Ordinal)];
        foreach (RequestDefinition request in requests)
        {
            if (request.Name is null || linesByName.TryAdd(request.Name, request.Line))
            {
                continue;
            }

            throw new HttpFileException(Messages.Format(
                s_duplicateRequestNameFormat,
                request.Name,
                linesByName[request.Name],
                request.Line));
        }
    }

    private readonly record struct SourceLine(int Number, int Start, int End);

    private sealed class RequestBuilder(
        string? name,
        string? title,
        string method,
        Template url,
        string? httpVersion,
        int line)
    {
        public string? Name { get; } = name;

        public string? Title { get; } = title;

        public string Method { get; } = method;

        public Template Url { get; } = url;

        public string? HttpVersion { get; } = httpVersion;

        public int Line { get; } = line;

        public List<HeaderDefinition> Headers { get; } = [];

        public bool IsInBody { get; set; }

        public SourceLine? FirstBodyLine { get; private set; }

        public SourceLine? LastBodyLine { get; private set; }

        public bool HasBodyText { get; private set; }

        public void AddBodyLine(SourceLine bodyLine, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            FirstBodyLine ??= bodyLine;
            LastBodyLine = bodyLine;
            HasBodyText |= !IsComment(text.TrimStart());
        }
    }
}
