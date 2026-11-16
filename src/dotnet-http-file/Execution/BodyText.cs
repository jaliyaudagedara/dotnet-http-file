using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;

namespace DotNetHttpFile.Execution;

/// <summary>
/// How the tool reads a body as text, shared by reference evaluation and display.
/// </summary>
internal static class BodyText
{
    private const int IndentSize = 2;

    /// <summary>
    /// Indented JSON for a person or a request to read, not for HTML, so non-ASCII text and &lt;, &gt;, &amp; and
    /// ' stay readable.
    /// </summary>
    public static JsonSerializerOptions IndentedJsonOptions { get; } = new()
    {
        WriteIndented = true,
        IndentSize = IndentSize,
        NewLine = Environment.NewLine,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Refuses a repeated property name while parsing. <see cref="System.Text.Json.Nodes.JsonObject"/> throws
    /// <see cref="ArgumentException"/> on one only when the object is first read.
    /// </summary>
    public static JsonDocumentOptions JsonDocumentOptions { get; } = new()
    {
        AllowDuplicateProperties = false,
    };

    /// <summary>
    /// Refuses a DOCTYPE outright, which rules out external entities and entity expansion.
    /// </summary>
    public static XmlReaderSettings XmlReaderSettings { get; } = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>
    /// Decodes <paramref name="body"/> with the charset of <paramref name="contentType"/>, or as UTF-8 when it
    /// names none or one that is unknown or unsupported. A byte order mark of that encoding is dropped.
    /// </summary>
    public static string Decode(string? contentType, ReadOnlyMemory<byte> body)
    {
        Encoding encoding = GetEncoding(contentType);
        ReadOnlySpan<byte> bytes = body.Span;

        // HttpContent.ReadAsStringAsync drops a byte order mark of the encoding in the same way.
        ReadOnlySpan<byte> preamble = encoding.Preamble;
        if (preamble.Length > 0 && bytes.StartsWith(preamble))
        {
            bytes = bytes[preamble.Length..];
        }

        return encoding.GetString(bytes);
    }

    private static Encoding GetEncoding(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? mediaType)
            || string.IsNullOrEmpty(mediaType.CharSet))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(mediaType.CharSet.Trim('"'));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }
}
