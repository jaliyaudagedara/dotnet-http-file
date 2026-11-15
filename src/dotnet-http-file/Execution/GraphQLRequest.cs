using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotNetHttpFile.Execution;

/// <summary>
/// Turns a request marked with <c>X-Request-Type: graphql</c> into the JSON request a GraphQL server reads.
/// </summary>
internal static partial class GraphQLRequest
{
    public const string ContentType = "application/json";

    private const string MarkerHeader = "X-Request-Type";
    private const string MarkerValue = "graphql";
    private const string ContentTypeHeader = "Content-Type";
    private const string QueryProperty = "query";
    private const string VariablesProperty = "variables";
    private const string OperationNameProperty = "operationName";

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
    /// The body is split at its first blank line: the query comes before it and the variables, a JSON object,
    /// after it.
    /// </remarks>
    /// <exception cref="HttpFileException">
    /// The query is empty, or the variables are not a JSON object. The message never holds the body.
    /// </exception>
    public static ResolvedRequest Apply(ResolvedRequest request, int line)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Headers.Any(IsMarker))
        {
            return request;
        }

        (string query, string variables) = Split(request.Body ?? string.Empty);
        if (query.Length == 0)
        {
            throw new HttpFileException(Format(s_queryMissingFormat, line));
        }

        using JsonDocument? variablesDocument = ParseVariables(variables, line);

        List<KeyValuePair<string, string>> headers = new(request.Headers.Count);
        List<int> headerLines = new(request.Headers.Count);
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

        if (!headers.Any(header => string.Equals(header.Key, ContentTypeHeader, StringComparison.OrdinalIgnoreCase)))
        {
            headers.Add(KeyValuePair.Create(ContentTypeHeader, ContentType));
            headerLines.Add(markerLine ?? line);
        }

        return request with
        {
            Headers = headers,
            Body = WriteEnvelope(query, variablesDocument, OperationName(query)),
            HeaderLines = headerLines,
        };
    }

    private static string Format(CompositeFormat format, params ReadOnlySpan<object?> arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);

    private static bool IsMarker(KeyValuePair<string, string> header) =>
        string.Equals(header.Key, MarkerHeader, StringComparison.OrdinalIgnoreCase)
        && string.Equals(header.Value.Trim(), MarkerValue, StringComparison.OrdinalIgnoreCase);

    private static (string Query, string Variables) Split(string body)
    {
        Match blankLine = BlankLinePattern().Match(body);

        return blankLine.Success
            ? (body[..blankLine.Index].Trim(), body[(blankLine.Index + blankLine.Length)..])
            : (body.Trim(), string.Empty);
    }

    private static JsonDocument? ParseVariables(string variables, int line)
    {
        if (string.IsNullOrWhiteSpace(variables))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(variables, BodyText.JsonDocumentOptions);
        }
        catch (JsonException)
        {
            throw new HttpFileException(Format(s_variablesNotObjectFormat, line));
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new HttpFileException(Format(s_variablesNotObjectFormat, line));
        }

        return document;
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
