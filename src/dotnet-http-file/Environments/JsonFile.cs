using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace DotNetHttpFile.Environments;

/// <summary>
/// Reads the JSON files a user writes by hand: the environment files and the user-secrets file.
/// </summary>
internal static class JsonFile
{
    // JsonObject throws ArgumentException on a repeated name only when it is first read, so parsing refuses it.
    private static readonly JsonDocumentOptions s_documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    /// <summary>
    /// Parses <paramref name="bytes"/>, which may start with a UTF-8 byte order mark and hold comments and
    /// trailing commas.
    /// </summary>
    /// <returns>
    /// False when <paramref name="bytes"/> are not valid UTF-8.
    /// </returns>
    /// <exception cref="JsonException">The text is not valid JSON, or an object repeats a property name.</exception>
    public static bool TryParse(ReadOnlySpan<byte> bytes, out JsonNode? root)
    {
        // The parser does not validate UTF-8 inside strings, so text in another encoding would parse and then
        // fail with InvalidOperationException on the first string read.
        if (!Utf8.IsValid(bytes))
        {
            root = null;

            return false;
        }

        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }

        root = JsonNode.Parse(bytes, nodeOptions: null, s_documentOptions);

        return true;
    }

    /// <summary>
    /// Gets the text of a string, number or Boolean: a string as it reads, a number or Boolean as it is written.
    /// </summary>
    /// <returns>
    /// The text, or null when <paramref name="node"/> is none of those.
    /// </returns>
    public static string? ScalarText(JsonNode? node) =>
        node is not JsonValue value
            ? null
            : value.GetValueKind() switch
            {
                JsonValueKind.String => value.GetValue<string>(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False =>
                    value.GetValue<JsonElement>().GetRawText(),
                _ => null,
            };
}
