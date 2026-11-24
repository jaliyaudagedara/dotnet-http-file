using System.Buffers;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.XPath;
using DotNetHttpFile.Execution;
using DotNetHttpFile.Templates;
using Json.Path;

namespace DotNetHttpFile.Resolution;

internal static class ReferenceEvaluator
{
    private const string WholeSelector = "*";
    private const char JsonPathPrefix = '$';
    private const char XPathPrefix = '/';
    private const string HeaderValueSeparator = ", ";
    private const string RequestKeyword = "request";
    private const string ResponseKeyword = "response";
    private const string JsonNull = "null";
    private const string XPathTrue = "true";
    private const string XPathFalse = "false";
    private const string RoundTripFormat = "R";

    private static readonly JsonWriterOptions s_compactOptions = new()
    {
        Encoder = BodyText.IndentedJsonOptions.Encoder,
    };

    /// <summary>
    /// Reads the value <paramref name="reference"/> selects from the request or response of
    /// <paramref name="exchange"/>, the exchange of the request the reference names.
    /// </summary>
    /// <remarks>
    /// A body selector is <c>*</c> for the whole text, a JSONPath starting with <c>$</c> or an XPath starting
    /// with <c>/</c>. The content type does not restrict which of them may be used.
    /// </remarks>
    /// <exception cref="HttpFileException">
    /// The selector is not supported or not valid, the response body is in a coding that is not decoded, the body
    /// is not in the format the selector needs, or the selector matches nothing. The message never holds body
    /// content.
    /// </exception>
    public static string Evaluate(RequestReferenceSegment reference, HttpExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(exchange);

        if (reference.Part == ReferencePart.Headers)
        {
            return EvaluateHeader(reference, exchange);
        }

        if (reference.Source == ReferenceSource.Response && exchange.Response.IsContentEncoded)
        {
            throw new HttpFileException(Messages.Format(
                Resources.ReferenceBodyNotDecoded,
                reference.RequestName,
                string.Join(Messages.ListSeparator, exchange.Response.UnsupportedCodings)));
        }

        string body = reference.Source == ReferenceSource.Request
            ? exchange.Request.Body ?? string.Empty
            : BodyText.Decode(exchange.Response.ContentType, exchange.Response.Body);

        if (reference.Selector == WholeSelector)
        {
            return body;
        }

        if (reference.Selector.StartsWith(JsonPathPrefix))
        {
            return EvaluateJsonPath(reference, body);
        }

        if (reference.Selector.StartsWith(XPathPrefix))
        {
            return EvaluateXPath(reference, body);
        }

        throw new HttpFileException(Messages.Format(Resources.ReferenceUnsupportedBodySelector, reference.Expression));
    }

    private static string EvaluateHeader(RequestReferenceSegment reference, HttpExchange exchange)
    {
        if (reference.Selector == WholeSelector)
        {
            throw new HttpFileException(Messages.Format(
                Resources.ReferenceAllHeadersUnsupported,
                reference.Expression));
        }

        IReadOnlyList<KeyValuePair<string, string>> headers = reference.Source == ReferenceSource.Request
            ? exchange.Request.Headers
            : exchange.Response.Headers;

        List<string> values = [];
        foreach (KeyValuePair<string, string> header in headers)
        {
            if (HeaderNames.Is(header, reference.Selector))
            {
                values.Add(header.Value);
            }
        }

        if (values.Count == 0)
        {
            throw new HttpFileException(Messages.Format(
                Resources.ReferenceHeaderNotFound,
                reference.RequestName,
                SourceKeyword(reference),
                reference.Selector));
        }

        return string.Join(HeaderValueSeparator, values);
    }

    private static string EvaluateJsonPath(RequestReferenceSegment reference, string body)
    {
        if (!JsonPath.TryParse(reference.Selector, out JsonPath? path))
        {
            throw new HttpFileException(
                Messages.Format(Resources.ReferenceInvalidJsonPath, reference.Selector, reference.Expression));
        }

        // System.Text.Json reports an escaped surrogate without its pair with InvalidOperationException, and
        // only when the text is first read as a string: while checking property names, comparing in a filter,
        // or writing the match.
        try
        {
            JsonNode? root = ParseJson(reference, body);

            JsonNode?[] matches;
            try
            {
                // The node list is copied once so that formatting does not depend on the library's types.
                matches = [.. path.Evaluate(root).Matches.Select(match => match.Value)];
            }
            catch (Exception exception) when (IsFilterFunctionFailure(exception))
            {
                throw new HttpFileException(
                    Messages.Format(Resources.ReferenceInvalidJsonPath, reference.Selector, reference.Expression),
                    exception);
            }

            if (IsProjection(path))
            {
                return FormatMatchesAsArray(matches);
            }

            return matches.Length switch
            {
                0 => throw new HttpFileException(
                    Messages.Format(Resources.ReferenceNoContentAtPath, reference.Expression)),
                1 => FormatSingleMatch(matches[0]),
                _ => FormatMatchesAsArray(matches),
            };
        }
        catch (InvalidOperationException exception)
        {
            throw new HttpFileException(
                Messages.Format(Resources.ReferenceBodyInvalidUnicode, reference.RequestName, SourceKeyword(reference)),
                exception);
        }
    }

    // Visual Studio 18.13 calls a wildcard path a projection and returns an array whatever the match count.
    // Other paths, filters and slices among them, keep returning a single match as the bare value.
    private static bool IsProjection(JsonPath path) =>
        path.Segments.Any(segment => segment.Selectors.Any(selector => selector is WildcardSelector));

    // A filter function such as match() or search() compiles its pattern while the path runs, so a bad
    // pattern fails here and not when the path is parsed.
    private static bool IsFilterFunctionFailure(Exception exception) =>
        exception is TargetInvocationException or ArgumentException or RegexMatchTimeoutException;

    private static JsonNode? ParseJson(RequestReferenceSegment reference, string body)
    {
        try
        {
            return JsonNode.Parse(body, documentOptions: BodyText.JsonDocumentOptions);
        }
        catch (JsonException exception)
        {
            string format = IsJson(body)
                ? Resources.ReferenceBodyDuplicateProperties
                : Resources.ReferenceBodyNotJson;

            throw new HttpFileException(
                Messages.Format(format, reference.RequestName, SourceKeyword(reference)),
                exception);
        }
    }

    private static bool IsJson(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string FormatSingleMatch(JsonNode? match) =>
        match switch
        {
            null => JsonNull,
            JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
            JsonValue value => value.ToJsonString(),
            _ => match.ToJsonString(BodyText.IndentedJsonOptions),
        };

    private static string FormatMatchesAsArray(JsonNode?[] matches)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer, s_compactOptions))
        {
            writer.WriteStartArray();
            foreach (JsonNode? match in matches)
            {
                if (match is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    match.WriteTo(writer);
                }
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string EvaluateXPath(RequestReferenceSegment reference, string body)
    {
        XPathNavigator navigator;
        try
        {
            using StringReader textReader = new(body);
            using XmlReader xmlReader = XmlReader.Create(textReader, BodyText.XmlReaderSettings);
            navigator = new XPathDocument(xmlReader).CreateNavigator();
        }
        catch (XmlException exception)
        {
            throw new HttpFileException(
                Messages.Format(Resources.ReferenceBodyNotXml, reference.RequestName, SourceKeyword(reference)),
                exception);
        }

        try
        {
            return navigator.Evaluate(reference.Selector) switch
            {
                XPathNodeIterator nodes => SingleNodeValue(reference, nodes),
                bool value => value ? XPathTrue : XPathFalse,
                double value => value.ToString(RoundTripFormat, CultureInfo.InvariantCulture),
                object value => (string)value,
            };
        }
        catch (XPathException exception)
        {
            throw new HttpFileException(
                Messages.Format(Resources.ReferenceInvalidXPath, reference.Selector, reference.Expression),
                exception);
        }
    }

    private static string SingleNodeValue(RequestReferenceSegment reference, XPathNodeIterator nodes)
    {
        int count = nodes.Count;
        if (count == 0)
        {
            throw new HttpFileException(Messages.Format(Resources.ReferenceNoContentAtPath, reference.Expression));
        }

        if (count > 1)
        {
            throw new HttpFileException(
                Messages.Format(Resources.ReferenceXPathMultipleNodes, reference.Selector, count));
        }

        nodes.MoveNext();

        return nodes.Current?.Value ?? string.Empty;
    }

    private static string SourceKeyword(RequestReferenceSegment reference) =>
        reference.Source == ReferenceSource.Request ? RequestKeyword : ResponseKeyword;
}
