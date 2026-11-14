using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;
using System.Xml;
using System.Xml.Linq;
using DotNetHttpFile.Execution;

namespace DotNetHttpFile.Output;

/// <summary>Decides whether a response body can be shown as text, and makes it readable.</summary>
internal static class BodyFormatter
{
    private const string TextTypePrefix = "text/";
    private const string JsonMediaType = "application/json";
    private const string XmlMediaType = "application/xml";
    private const string JavaScriptMediaType = "application/javascript";
    private const string FormMediaType = "application/x-www-form-urlencoded";
    private const string JsonSuffix = "+json";
    private const string XmlSuffix = "+xml";
    private const string XmlIndent = "  ";

    // The indentation an XML writer adds grows with the square of the nesting depth, so a small body that is
    // nested thousands of levels deep would cost hundreds of megabytes to indent.
    private const int MaxXmlDepth = 64;

    private static readonly JsonWriterOptions s_jsonWriterOptions = new()
    {
        Indented = BodyText.IndentedJsonOptions.WriteIndented,
        IndentSize = BodyText.IndentedJsonOptions.IndentSize,
        NewLine = BodyText.IndentedJsonOptions.NewLine,
        Encoder = BodyText.IndentedJsonOptions.Encoder,
    };

    private static readonly XmlWriterSettings s_xmlWriterSettings = new()
    {
        Indent = true,
        IndentChars = XmlIndent,
        NewLineChars = Environment.NewLine,

        // A writer over a string would declare UTF-16, so the declaration of the document is written instead.
        OmitXmlDeclaration = true,
    };

    /// <summary>
    /// Whether a body of <paramref name="contentType"/> is text. Without a usable content type the body is text
    /// when it is valid UTF-8 and holds no null character.
    /// </summary>
    public static bool IsText(string? contentType, ReadOnlySpan<byte> body)
    {
        string? mediaType = MediaType(contentType);
        if (mediaType is null)
        {
            return Utf8.IsValid(body) && !body.Contains((byte)0);
        }

        return mediaType.StartsWith(TextTypePrefix, StringComparison.OrdinalIgnoreCase)
            || IsJson(mediaType)
            || IsXml(mediaType)
            || mediaType.Equals(JavaScriptMediaType, StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals(FormMediaType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decodes a text body, hides secrets in it with <paramref name="mask"/> and indents it when it is JSON or
    /// XML. A body that is not valid for its type, or that cannot be rewritten without changing it, is
    /// returned as it was decoded, masked.
    /// </summary>
    /// <remarks>
    /// Rewriting a body changes how its values are escaped, so a mask over the rewritten text alone would miss
    /// a secret that the body spells one way and the writer another. The text is therefore masked as it was
    /// received, which catches a secret written out verbatim, and every decoded string of the parsed body is
    /// masked again before it is written. The caller still masks the returned text as a whole.
    /// </remarks>
    public static string FormatForDisplay(string? contentType, ReadOnlyMemory<byte> body, Func<string, string> mask)
    {
        ArgumentNullException.ThrowIfNull(mask);

        string text = mask(BodyText.Decode(contentType, body));
        string? mediaType = MediaType(contentType);
        if (mediaType is null)
        {
            return text;
        }

        if (IsJson(mediaType))
        {
            return IndentJson(text, mask) ?? text;
        }

        if (IsXml(mediaType))
        {
            return IndentXml(text, mask) ?? text;
        }

        return text;
    }

    private static string? MediaType(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? parsed) ? parsed.MediaType : null;

    private static bool IsJson(string mediaType) =>
        mediaType.Equals(JsonMediaType, StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith(JsonSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsXml(string mediaType) =>
        mediaType.Equals(XmlMediaType, StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith(XmlSuffix, StringComparison.OrdinalIgnoreCase);

    private static string? IndentJson(string text, Func<string, string> mask)
    {
        // A repeated property name fails as JsonException while parsing. An escaped surrogate without its pair
        // fails as InvalidOperationException, and only when the string is read.
        try
        {
            using JsonDocument document = JsonDocument.Parse(text, BodyText.JsonDocumentOptions);

            ArrayBufferWriter<byte> buffer = new();
            using (Utf8JsonWriter writer = new(buffer, s_jsonWriterOptions))
            {
                WriteMasked(writer, document.RootElement, mask);
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    // The parser refuses a document nested deeper than its default limit of 64, which bounds this recursion.
    private static void WriteMasked(Utf8JsonWriter writer, JsonElement element, Func<string, string> mask)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    writer.WritePropertyName(mask(property.Name));
                    WriteMasked(writer, property.Value, mask);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteMasked(writer, item, mask);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(mask(element.GetString() ?? string.Empty));
                break;

            default:
                // Writes a number with the text it was received with.
                element.WriteTo(writer);
                break;
        }
    }

    private static string? IndentXml(string text, Func<string, string> mask)
    {
        try
        {
            if (IsNestedTooDeep(text))
            {
                return null;
            }

            using StringReader textReader = new(text);
            using XmlReader xmlReader = XmlReader.Create(textReader, BodyText.XmlReaderSettings);
            XDocument document = XDocument.Load(xmlReader);
            MaskValues(document, mask);

            StringBuilder builder = new();
            if (document.Declaration is not null)
            {
                builder.Append(document.Declaration).Append(Environment.NewLine);
            }

            using (XmlWriter xmlWriter = XmlWriter.Create(builder, s_xmlWriterSettings))
            {
                document.Save(xmlWriter);
            }

            return builder.ToString();
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsNestedTooDeep(string text)
    {
        using StringReader textReader = new(text);
        using XmlReader xmlReader = XmlReader.Create(textReader, BodyText.XmlReaderSettings);
        while (xmlReader.Read())
        {
            // The root element is at depth 0.
            if (xmlReader.NodeType == XmlNodeType.Element && xmlReader.Depth >= MaxXmlDepth)
            {
                return true;
            }
        }

        return false;
    }

    private static void MaskValues(XDocument document, Func<string, string> mask)
    {
        foreach (XElement element in document.Descendants().ToArray())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                if (!attribute.IsNamespaceDeclaration)
                {
                    attribute.Value = mask(attribute.Value);
                }
            }

            MaskText(element, mask);
        }
    }

    // Text and CDATA sections that follow one another read as one text, so they are masked as one. The child
    // list is rebuilt and put back in one step: taking nodes out one at a time walks the list on every call.
    private static void MaskText(XElement element, Func<string, string> mask)
    {
        List<XNode> nodes = [];
        List<XText> run = [];
        bool isRunReplaced = false;
        foreach (XNode node in element.Nodes())
        {
            if (node is XText content)
            {
                run.Add(content);
                continue;
            }

            isRunReplaced |= AddMaskedRun(nodes, run, mask);
            run.Clear();
            nodes.Add(node);
        }

        isRunReplaced |= AddMaskedRun(nodes, run, mask);
        if (isRunReplaced)
        {
            element.ReplaceNodes(nodes);
        }
    }

    /// <summary>Adds the masked run to <paramref name="nodes"/>. Returns whether it added a new node.</summary>
    private static bool AddMaskedRun(List<XNode> nodes, List<XText> run, Func<string, string> mask)
    {
        if (run.Count == 1)
        {
            // Changing the value in place keeps a CDATA section a CDATA section.
            run[0].Value = mask(run[0].Value);
            nodes.Add(run[0]);

            return false;
        }

        if (run.Count == 0)
        {
            return false;
        }

        string text = string.Concat(run.Select(content => content.Value));
        string masked = mask(text);
        if (masked == text)
        {
            nodes.AddRange(run);

            return false;
        }

        nodes.Add(new XText(masked));

        return true;
    }
}
