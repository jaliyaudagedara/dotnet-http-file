using System.Buffers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotNetHttpFile.Execution;

/// <summary>
/// Turns a request marked with <c>X-Request-Type: graphql</c> into the JSON request a GraphQL server reads.
/// </summary>
internal static partial class GraphQLRequest
{
    public const string ContentType = MediaTypeNames.Application.Json;

    private const string MarkerHeader = "X-Request-Type";
    private const string MarkerValue = "graphql";
    private const string QueryProperty = "query";
    private const string VariablesProperty = "variables";
    private const string OperationNameProperty = "operationName";
    private const char OpeningBrace = '{';
    private const char ClosingBrace = '}';
    private const char StringQuote = '"';

    private static readonly CompositeFormat s_queryMissingFormat = CompositeFormat.Parse(Resources.GraphQLQueryMissing);

    private static readonly CompositeFormat s_variablesNotObjectFormat =
        CompositeFormat.Parse(Resources.GraphQLVariablesNotObject);

    // The same encoder the secret mask registers JSON forms with, so a secret escaped into the query still matches.
    private static readonly JsonWriterOptions s_writerOptions = new()
    {
        Encoder = BodyText.IndentedJsonOptions.Encoder,
    };

    /// <summary>
    /// Returns <paramref name="request"/> unchanged when it has no marker header. Otherwise returns it with the
    /// body as <c>{"query", "variables", "operationName"}</c>, the marker header left out and a
    /// <c>Content-Type</c> of <see cref="ContentType"/> when the file writes none.
    /// </summary>
    /// <param name="request">The resolved request, whose header lines are parallel to its headers.</param>
    /// <param name="line">The line of the request line, named in errors.</param>
    /// <remarks>
    /// The variables are the text after a blank line of the body that is a JSON object, trying the last blank
    /// line first and then each one before it, so that variables may hold blank lines too. The query is the
    /// text before that blank line. When no such text is a JSON object the whole body is the query, so a query
    /// may hold blank lines of its own. Then a block after a blank line that starts as a JSON object does (a
    /// brace, then a quote or the closing brace) is taken as variables with a mistake in them, and is an error.
    /// </remarks>
    /// <exception cref="HttpFileException">
    /// The query is empty, or no block after a blank line is a JSON object and one of them starts as a JSON object
    /// does. The message never holds the body.
    /// </exception>
    public static ResolvedRequest Apply(ResolvedRequest request, int line)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Headers.Any(IsMarker))
        {
            return request;
        }

        (string query, JsonDocument? variables) = Split(request.Body ?? string.Empty, line);
        using JsonDocument? variablesDocument = variables;
        if (query.Length == 0)
        {
            throw new HttpFileException(Messages.Format(s_queryMissingFormat, line));
        }

        List<KeyValuePair<string, string>> headers = [with(capacity: request.Headers.Count)];
        List<int> headerLines = [with(capacity: request.Headers.Count)];
        int? markerLine = null;
        for (int index = 0; index < request.Headers.Count; index++)
        {
            if (IsMarker(request.Headers[index]))
            {
                markerLine ??= request.HeaderLines[index];
                continue;
            }

            headers.Add(request.Headers[index]);
            headerLines.Add(request.HeaderLines[index]);
        }

        if (!headers.Any(header => HeaderNames.Is(header, HeaderNames.ContentType)))
        {
            headers.Add(KeyValuePair.Create(HeaderNames.ContentType, ContentType));
            headerLines.Add(markerLine ?? line);
        }

        return request with
        {
            Headers = headers,
            Body = WriteEnvelope(query, variablesDocument, OperationName(query)),
            HeaderLines = headerLines,
        };
    }

    private static bool IsMarker(KeyValuePair<string, string> header) =>
        HeaderNames.Is(header, MarkerHeader)
        && string.Equals(header.Value.Trim(), MarkerValue, StringComparison.OrdinalIgnoreCase);

    private static (string Query, JsonDocument? Variables) Split(string body, int line)
    {
        // Only the end: a body that starts with a blank line has no query, and that must stay visible.
        body = body.TrimEnd();
        MatchCollection blankLines = BlankLinePattern().Matches(body);

        // Variables can hold blank lines of their own, so each blank line is tried, from the last one back.
        for (int index = blankLines.Count - 1; index >= 0; index--)
        {
            Match blankLine = blankLines[index];
            JsonDocument? variables = ParseObject(TextAfter(body, blankLine));
            if (variables is not null)
            {
                return (body[..blankLine.Index].Trim(), variables);
            }
        }

        // A fragment, a second operation or an anonymous query after a blank line is part of the query. A block
        // that starts as a JSON object does is variables with a mistake in them, which sending it as GraphQL would
        // hide. Not only the last block: the variables may hold a blank line of their own.
        if (blankLines.Any(blankLine => StartsLikeJsonObject(TextAfter(body, blankLine))))
        {
            throw new HttpFileException(Messages.Format(s_variablesNotObjectFormat, line));
        }

        return (body.TrimStart(), null);
    }

    private static string TextAfter(string body, Match blankLine) => body[(blankLine.Index + blankLine.Length)..];

    private static JsonDocument? ParseObject(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, BodyText.JsonDocumentOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            return document;
        }

        document.Dispose();

        return null;
    }

    // A JSON object goes on from its brace with a quoted name or its closing brace. A GraphQL selection set goes
    // on with a name or a spread.
    private static bool StartsLikeJsonObject(ReadOnlySpan<char> text)
    {
        text = text.TrimStart();

        return text is [OpeningBrace, ..]
            && text[1..].TrimStart() is [StringQuote or ClosingBrace, ..];
    }

    private static string? OperationName(string query)
    {
        Match match = NamedOperationPattern().Match(query);

        return match.Success ? match.Groups["name"].Value : null;
    }

    private static string WriteEnvelope(string query, JsonDocument? variables, string? operationName)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer, s_writerOptions))
        {
            writer.WriteStartObject();
            writer.WriteString(QueryProperty, query);
            if (variables is not null)
            {
                writer.WritePropertyName(VariablesProperty);
                variables.RootElement.WriteTo(writer);
            }

            if (operationName is not null)
            {
                writer.WriteString(OperationNameProperty, operationName);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // Atomic, so that the '\r' of a "\r\n" is never taken as a line break of its own before the '\n'.
    [GeneratedRegex(@"(?>\r\n|\r|\n)[^\S\r\n]*(?>\r\n|\r|\n)", RegexOptions.CultureInvariant)]
    private static partial Regex BlankLinePattern();

    // Leading '#' lines are GraphQL comments. A keyword followed by '{' or '(' is an anonymous operation.
    [GeneratedRegex(
        @"\A(?:[^\S\r\n]*#[^\r\n]*(?>\r\n|\r|\n))*\s*"
            + @"(?:query|mutation|subscription)\s+(?<name>[_A-Za-z][_0-9A-Za-z]*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamedOperationPattern();
}
