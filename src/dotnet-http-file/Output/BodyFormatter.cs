using System.Buffers;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;
using System.Xml;
using System.Xml.Linq;
using DotNetHttpFile.Execution;

namespace DotNetHttpFile.Output;

/// <summary>
/// Decides whether a response body can be shown as text, and makes it readable.
/// </summary>
internal static class BodyFormatter
{
    private const string TextTypePrefix = "text/";
    private const string JavaScriptMediaType = "application/javascript";
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
            || mediaType.Equals(MediaTypeNames.Application.FormUrlEncoded, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decodes a text body and indents it when it is JSON or XML. A body that is not valid for its type, or that
    /// cannot be rewritten without changing it, is returned as it was decoded.
    /// </summary>
    public static string FormatForDisplay(string? contentType, ReadOnlyMemory<byte> body)
    {
        string text = BodyText.Decode(contentType, body);
        string? mediaType = MediaType(contentType);
        if (mediaType is null)
        {
            return text;
        }

        if (IsJson(mediaType))
        {
            return IndentJson(text) ?? text;
        }

        if (IsXml(mediaType))
        {
            return IndentXml(text) ?? text;
        }

        return text;
    }

    private static string? MediaType(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? parsed) ? parsed.MediaType : null;

    private static bool IsJson(string mediaType) =>
        mediaType.Equals(MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith(JsonSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsXml(string mediaType) =>
        mediaType.Equals(MediaTypeNames.Application.Xml, StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals(MediaTypeNames.Text.Xml, StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith(XmlSuffix, StringComparison.OrdinalIgnoreCase);

    private static string? IndentJson(string text)
    {
        // A repeated property name fails as JsonException while parsing. An escaped surrogate without its pair
        // fails as InvalidOperationException, and only when the string is read.
        try
        {
            using JsonDocument document = JsonDocument.Parse(text, BodyText.JsonDocumentOptions);

            ArrayBufferWriter<byte> buffer = new();
            using (Utf8JsonWriter writer = new(buffer, s_jsonWriterOptions))
            {
                // Writes each number with the text it was received with.
                document.RootElement.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? IndentXml(string text)
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
}
