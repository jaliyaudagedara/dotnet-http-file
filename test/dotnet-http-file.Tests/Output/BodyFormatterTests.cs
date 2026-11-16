using System.Text;
using DotNetHttpFile.Output;

namespace DotNetHttpFile.Tests.Output;

public sealed class BodyFormatterTests
{
    // The letter b as a JSON escape, put together here so that the source holds no escape of its own.
    private const string EscapedB = @"\" + "u0062";

    private static readonly string s_newLine = Environment.NewLine;

    [Fact]
    public void IsText_JsonSuffixType_ReturnsTrue()
    {
        bool isText = BodyFormatter.IsText("application/problem+json", [0xFF, 0x00]);

        Assert.True(isText);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html; charset=utf-8")]
    [InlineData("application/json")]
    [InlineData("APPLICATION/JSON; charset=utf-8")]
    [InlineData("application/xml")]
    [InlineData("application/atom+xml")]
    [InlineData("image/svg+xml")]
    [InlineData("application/javascript")]
    [InlineData("application/x-www-form-urlencoded")]
    public void IsText_TextType_ReturnsTrue(string contentType)
    {
        bool isText = BodyFormatter.IsText(contentType, [0xFF, 0x00]);

        Assert.True(isText);
    }

    [Theory]
    [InlineData("application/octet-stream")]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    [InlineData("application/jsonx")]
    public void IsText_OtherType_ReturnsFalse(string contentType)
    {
        bool isText = BodyFormatter.IsText(contentType, "plain"u8);

        Assert.False(isText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a content type")]
    public void IsText_NoUsableContentTypeAndValidUtf8_ReturnsTrue(string? contentType)
    {
        bool isText = BodyFormatter.IsText(contentType, Encoding.UTF8.GetBytes("caf\u00E9"));

        Assert.True(isText);
    }

    [Fact]
    public void IsText_NoContentTypeAndInvalidUtf8_ReturnsFalse()
    {
        bool isText = BodyFormatter.IsText(null, [0xFF, 0xFE, 0x41]);

        Assert.False(isText);
    }

    [Fact]
    public void IsText_NoContentTypeAndNullCharacter_ReturnsFalse()
    {
        bool isText = BodyFormatter.IsText(null, [0x41, 0x00, 0x42]);

        Assert.False(isText);
    }

    [Fact]
    public void FormatForDisplay_Json_IndentsWithTwoSpaces()
    {
        string text = Format("application/json", """{"id":1,"tags":["a","b"]}""");

        Assert.Equal(
            string.Join(s_newLine, "{", "  \"id\": 1,", "  \"tags\": [", "    \"a\",", "    \"b\"", "  ]", "}"),
            text);
    }

    [Fact]
    public void FormatForDisplay_JsonSuffixType_Indents()
    {
        string text = Format("application/problem+json; charset=utf-8", """{"status":404}""");

        Assert.Equal($"{{{s_newLine}  \"status\": 404{s_newLine}}}", text);
    }

    [Fact]
    public void FormatForDisplay_JsonWithNonAsciiAndHtmlCharacters_KeepsThemReadable()
    {
        string text = Format("application/json", "[\"caf\u00E9 <b> & 'x'\"]");

        Assert.Equal($"[{s_newLine}  \"caf\u00E9 <b> & 'x'\"{s_newLine}]", text);
    }

    [Theory]
    [InlineData("3.50")]
    [InlineData("1e2")]
    [InlineData("12345678901234567890123")]
    public void FormatForDisplay_JsonNumber_KeepsOriginalText(string number)
    {
        string text = Format("application/json", $"[{number}]");

        Assert.Equal($"[{s_newLine}  {number}{s_newLine}]", text);
    }

    [Fact]
    public void FormatForDisplay_JsonEscapesInNamesAndStrings_AreWrittenDecoded()
    {
        string text = Format("application/json", $$"""{"a{{EscapedB}}c":["x-a{{EscapedB}}c-y","q\"\\\/"]}""");

        Assert.Equal(
            string.Join(s_newLine, "{", "  \"abc\": [", "    \"x-abc-y\",", "    \"q\\\"\\\\/\"", "  ]", "}"),
            text);
    }

    [Fact]
    public void FormatForDisplay_JsonRepeatedNameAfterDecoding_ReturnsAsIs()
    {
        string body = $$"""{"abc":1,"a{{EscapedB}}c":2}""";

        string text = Format("application/json", body);

        Assert.Equal(body, text);
    }

    [Fact]
    public void FormatForDisplay_InvalidJson_ReturnsAsIs()
    {
        string text = Format("application/json", """{"id": 1,""");

        Assert.Equal("""{"id": 1,""", text);
    }

    [Theory]
    [InlineData("""{"a":1,"a":2}""")]
    [InlineData("""{"a":"\ud800"}""")]
    [InlineData("null")]
    [InlineData("")]
    public void FormatForDisplay_JsonThatCannotBeRewritten_ReturnsAsIs(string body)
    {
        string text = Format("application/json", body);

        Assert.Equal(body, text);
    }

    [Fact]
    public void FormatForDisplay_Xml_Indents()
    {
        string text = Format("application/xml", "<root><item id=\"1\">a</item><empty/></root>");

        Assert.Equal($"<root>{s_newLine}  <item id=\"1\">a</item>{s_newLine}  <empty />{s_newLine}</root>", text);
    }

    [Fact]
    public void FormatForDisplay_XmlWithDeclaration_KeepsDeclaration()
    {
        string text = Format("application/soap+xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><root><a/></root>");

        Assert.Equal(
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?>{s_newLine}<root>{s_newLine}  <a />{s_newLine}</root>",
            text);
    }

    [Fact]
    public void FormatForDisplay_XmlTextCDataAndAttributes_KeepTheirForm()
    {
        string text = Format(
            "application/xml",
            "<r xmlns:p=\"urn:a&amp;b\" k=\"a&#38;b\"><t>a&#x26;b</t><c><![CDATA[z<z]]></c><s>a&amp;<![CDATA[b<]]>c</s></r>");

        Assert.Equal(
            string.Join(
                s_newLine,
                "<r xmlns:p=\"urn:a&amp;b\" k=\"a&amp;b\">",
                "  <t>a&amp;b</t>",
                "  <c><![CDATA[z<z]]></c>",
                "  <s>a&amp;<![CDATA[b<]]>c</s>",
                "</r>"),
            text);
    }

    [Theory]
    [InlineData("<root><open></root>")]
    [InlineData("<!DOCTYPE root [<!ENTITY e \"x\">]><root>&e;</root>")]
    [InlineData("")]
    public void FormatForDisplay_XmlThatCannotBeRewritten_ReturnsAsIs(string body)
    {
        string text = Format("application/xml", body);

        Assert.Equal(body, text);
    }

    [Fact]
    public void FormatForDisplay_PlainText_ReturnsAsIs()
    {
        string text = Format("text/plain", """{"id":1}""");

        Assert.Equal("""{"id":1}""", text);
    }

    [Fact]
    public void FormatForDisplay_CharsetInContentType_DecodesWithIt()
    {
        byte[] body = [0x63, 0x61, 0x66, 0xE9];

        string text = BodyFormatter.FormatForDisplay("text/plain; charset=iso-8859-1", body);

        Assert.Equal("caf\u00E9", text);
    }

    [Fact]
    public void FormatForDisplay_Utf8Bom_StripsBom()
    {
        byte[] body = [.. Encoding.UTF8.GetPreamble(), .. "[1]"u8];

        string text = BodyFormatter.FormatForDisplay("application/json", body);

        Assert.Equal($"[{s_newLine}  1{s_newLine}]", text);
    }

    [Fact]
    public void FormatForDisplay_XmlNestedToTheDepthLimit_Indents()
    {
        string body = NestedXml(depth: 64);

        string text = Format("application/xml", body);

        Assert.NotEqual(body, text);
        Assert.Equal(127, text.Split(s_newLine).Length);
    }

    [Fact]
    public void FormatForDisplay_XmlNestedDeeperThanTheLimit_ReturnsAsIs()
    {
        string body = NestedXml(depth: 65);

        string text = Format("application/xml", body);

        Assert.Equal(body, text);
    }

    [Fact]
    public void FormatForDisplay_XmlNestedThousandsDeep_ReturnsAsIsWithoutIndenting()
    {
        string body = NestedXml(depth: 5000);

        string text = Format("application/xml", body);

        Assert.Equal(body, text);
    }

    private static string NestedXml(int depth) =>
        string.Concat(Enumerable.Repeat("<a>", depth)) + string.Concat(Enumerable.Repeat("</a>", depth));

    private static string Format(string contentType, string body) =>
        BodyFormatter.FormatForDisplay(contentType, Encoding.UTF8.GetBytes(body));
}
