using System.Text;
using DotNetHttpFile.Execution;
using DotNetHttpFile.Resolution;
using DotNetHttpFile.Templates;

namespace DotNetHttpFile.Tests.Resolution;

public sealed class ReferenceEvaluatorTests
{
    private const string JsonContentType = "application/json; charset=utf-8";
    private const string XmlContentType = "application/xml; charset=utf-8";
    private const string ContentTypeHeader = "Content-Type";

    private const string CaptureJson =
        """{"str": "abc def", "num": 42, "dec": 3.5, "bool": true, "nul": null, "obj": {"a": 1, "b": "two"}, "arr": [1, 2, 3], "items": [{"id": 1}, {"id": 2}]}""";

    private const string CaptureXml =
        """<?xml version="1.0" encoding="utf-8"?><root><token>xml-token-123</token><n>7</n></root>""";

    private const string NotXmlMessage =
        "'xml' response body is not well-formed XML. A body with a DOCTYPE is not supported.";

    private static readonly string s_newLine = Environment.NewLine;

    [Fact]
    public void Evaluate_StringScalar_ReturnsUnquoted()
    {
        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body.$.str"), JsonExchange());

        Assert.Equal("abc def", value);
    }

    [Theory]
    [InlineData("$.num", "42")]
    [InlineData("$.dec", "3.5")]
    [InlineData("$.bool", "true")]
    [InlineData("$.nul", "null")]
    public void Evaluate_NumberBoolNull_ReturnJsonText(string selector, string expected)
    {
        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body." + selector), JsonExchange());

        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("""{"n": 3.50}""", "3.50")]
    [InlineData("""{"n": 1e2}""", "1e2")]
    [InlineData("""{"n": 12345678901234567890123}""", "12345678901234567890123")]
    public void Evaluate_Number_KeepsOriginalJsonText(string body, string expected)
    {
        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body.$.n"), JsonExchange(body));

        Assert.Equal(expected, value);
    }

    [Fact]
    public void Evaluate_Object_ReturnsIndentedJson()
    {
        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body.$.obj"), JsonExchange());

        Assert.Equal($"{{{s_newLine}  \"a\": 1,{s_newLine}  \"b\": \"two\"{s_newLine}}}", value);
    }

    [Fact]
    public void Evaluate_Array_ReturnsIndentedJson()
    {
        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body.$.arr"), JsonExchange());

        Assert.Equal($"[{s_newLine}  1,{s_newLine}  2,{s_newLine}  3{s_newLine}]", value);
    }

    [Fact]
    public void Evaluate_ArrayElement_ReturnsIndentedJson()
    {
        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body.$.items[0]"), JsonExchange());

        Assert.Equal($"{{{s_newLine}  \"id\": 1{s_newLine}}}", value);
    }

    [Fact]
    public void Evaluate_RootArrayIndex_ReturnsElementProperty()
    {
        string value = ReferenceEvaluator.Evaluate(
            Reference("json.response.body.$[0].ID"),
            JsonExchange("""[{"ID":11},{"ID":22}]"""));

        Assert.Equal("11", value);
    }

    [Fact]
    public void Evaluate_MultipleMatches_ReturnsCompactArray()
    {
        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body.$.items[*].id"), JsonExchange());

        Assert.Equal("[1,2]", value);
    }

    [Fact]
    public void Evaluate_MultipleMatchesOfMixedKinds_ReturnsCompactArray()
    {
        string value = ReferenceEvaluator.Evaluate(
            Reference("json.response.body.$[*]"),
            JsonExchange("""[null, {"a": "<b>"}, "caf\u00E9", 1.50]"""));

        Assert.Equal("[null,{\"a\":\"<b>\"},\"caf\u00E9\",1.50]", value);
    }

    [Theory]
    [InlineData("$.items[*].id", """{"items": []}""", "[]")]
    [InlineData("$.items[*].id", """{"items": [{"id": 1}]}""", "[1]")]
    [InlineData("$.items[*]", """{"items": [{"id": "café"}]}""", "[{\"id\":\"café\"}]")]
    [InlineData("$.obj.*", """{"obj": {"a": "x"}}""", "[\"x\"]")]
    [InlineData("$.obj[*]", """{"obj": {}}""", "[]")]
    [InlineData("$.items[*].id", """{"items": [{"id": 1}, {"id": 2}]}""", "[1,2]")]
    [InlineData("$..*", """{"a": 1}""", "[1]")]
    public void Evaluate_WildcardProjection_ReturnsCompactArrayForAnyMatchCount(
        string selector,
        string body,
        string expected)
    {
        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body." + selector), JsonExchange(body));

        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("$.items[?@.id == 1].id", "1")]
    [InlineData("$.arr[0:1]", "1")]
    [InlineData("$..b", "two")]
    [InlineData("$.items[?@.tags[*]].id", "1")]
    public void Evaluate_NonWildcardPathWithOneMatch_ReturnsBareValue(string selector, string expected)
    {
        string value = ReferenceEvaluator.Evaluate(
            Reference("json.response.body." + selector),
            JsonExchange("""{"items": [{"id": 1, "tags": ["x"]}, {"id": 2}], "arr": [1, 2], "obj": {"b": "two"}}"""));

        Assert.Equal(expected, value);
    }

    [Fact]
    public void Evaluate_FilterWithNoMatch_ThrowsVsMessage()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("json.response.body.$.items[?@.id == 9]"), JsonExchange()));

        Assert.Equal(
            "The named request does not contain any content at this path 'json.response.body.$.items[?@.id == 9]'.",
            exception.Message);
    }

    [Fact]
    public void Evaluate_ObjectWithNonAsciiAndHtmlCharacters_LeavesThemUnescaped()
    {
        string value = ReferenceEvaluator.Evaluate(
            Reference("json.response.body.$.obj"),
            JsonExchange("""{"obj": {"a": "caf\u00E9 <b> & 'c'"}}"""));

        Assert.Equal($"{{{s_newLine}  \"a\": \"caf\u00E9 <b> & 'c'\"{s_newLine}}}", value);
    }

    [Fact]
    public void Evaluate_RootSelector_ReturnsIndentedBody()
    {
        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body.$"), JsonExchange("""{"a":1}"""));

        Assert.Equal($"{{{s_newLine}  \"a\": 1{s_newLine}}}", value);
    }

    [Fact]
    public void Evaluate_RootSelectorOverNullBody_ReturnsNull()
    {
        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body.$"), JsonExchange("null"));

        Assert.Equal("null", value);
    }

    [Fact]
    public void Evaluate_NoMatch_ThrowsVsMessage()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("json.response.body.$.nope"), JsonExchange()));

        Assert.Equal(
            "The named request does not contain any content at this path 'json.response.body.$.nope'.",
            exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>secret-token</html>")]
    [InlineData("""{"token": "secret-token" """)]
    public void Evaluate_BodyNotJson_ThrowsWithoutBodyContent(string body)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("json.response.body.$.token"), JsonExchange(body)));

        Assert.Equal("'json' response body is not JSON.", exception.Message);
    }

    [Theory]
    [InlineData("""{"a": 1, "a": 2}""")]
    [InlineData("""{"token": "secret-token", "b": [{"c": 1, "c": 2}]}""")]
    public void Evaluate_BodyWithRepeatedPropertyName_ThrowsWithoutBodyContent(string body)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("json.response.body.$.token"), JsonExchange(body)));

        Assert.Equal(
            "'json' response body has a JSON object with a repeated property name, which a JSONPath cannot read.",
            exception.Message);
    }

    [Theory]
    [InlineData("""{"\ud800": 1, "a": "secret-token"}""", "$.a")]
    [InlineData("""{"a": "\ud800"}""", "$.a")]
    [InlineData("""{"a": {"b": "\ud800"}}""", "$.a")]
    [InlineData("""{"a": {"\ud800": 1}}""", "$.a")]
    [InlineData("""["\ud800", "secret-token"]""", "$[*]")]
    [InlineData("""[{"a": "\ud800"}, {"a": "secret-token"}]""", "$[?@.a == 'x']")]
    public void Evaluate_BodyWithLoneSurrogate_ThrowsWithoutBodyContent(string body, string selector)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("json.response.body." + selector), JsonExchange(body)));

        Assert.Equal("'json' response body has JSON text that is not valid Unicode.", exception.Message);
    }

    [Theory]
    [InlineData("$.items[?search(@.r, '[')]")]
    [InlineData("$.items[?match('a', @.r)]")]
    public void Evaluate_JsonPathFilterWithInvalidRegex_ThrowsNamingExpression(string selector)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(
                Reference("json.response.body." + selector),
                JsonExchange("""{"items": [{"r": "["}]}""")));

        Assert.Equal($"Invalid JSONPath '{selector}' in 'json.response.body.{selector}'.", exception.Message);
    }

    [Fact]
    public void Evaluate_InvalidJsonPath_ThrowsNamingExpression()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("json.response.body.$[?"), JsonExchange()));

        Assert.Equal("Invalid JSONPath '$[?' in 'json.response.body.$[?'.", exception.Message);
    }

    [Fact]
    public void Evaluate_WholeBody_ReturnsExactText()
    {
        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body.*"), JsonExchange());

        Assert.Equal(CaptureJson, value);
    }

    [Fact]
    public void Evaluate_WholeBodyWithCharset_DecodesWithThatCharset()
    {
        HttpExchange exchange = Exchange(Response("text/plain; charset=iso-8859-1", [0x63, 0x61, 0x66, 0xE9]));

        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body.*"), exchange);

        Assert.Equal("caf\u00E9", value);
    }

    [Theory]
    [InlineData("text/plain; charset=\"utf-8\"")]
    [InlineData("text/plain; charset=no-such-charset")]
    [InlineData("text/plain; charset=utf-7")]
    [InlineData("text/plain")]
    [InlineData("not a media type")]
    [InlineData(null)]
    public void Evaluate_WholeBodyWithoutUsableCharset_DecodesAsUtf8(string? contentType)
    {
        HttpExchange exchange = Exchange(Response(contentType, Encoding.UTF8.GetBytes("caf\u00E9")));

        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body.*"), exchange);

        Assert.Equal("caf\u00E9", value);
    }

    [Fact]
    public void Evaluate_WholeBodyWithUtf8Bom_StripsBom()
    {
        byte[] body = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(CaptureJson)];

        string value = ReferenceEvaluator.Evaluate(
            Reference("json.response.body.*"),
            Exchange(Response(JsonContentType, body)));

        Assert.Equal(CaptureJson, value);
    }

    [Fact]
    public void Evaluate_WholeBodyUtf16WithBom_StripsBom()
    {
        byte[] body = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("abc")];

        string value = ReferenceEvaluator.Evaluate(
            Reference("json.response.body.*"),
            Exchange(Response("text/plain; charset=utf-16", body)));

        Assert.Equal("abc", value);
    }

    [Fact]
    public void Evaluate_HeaderCaseInsensitive_ReturnsFullValue()
    {
        string value = ReferenceEvaluator.Evaluate(Reference("json.response.headers.content-type"), JsonExchange());

        Assert.Equal(JsonContentType, value);
    }

    [Fact]
    public void Evaluate_HeaderWithSeveralValues_JoinsWithCommaSpace()
    {
        ReceivedResponse response = Response(
            [
                new("Set-Cookie", "a=1"),
                new(ContentTypeHeader, JsonContentType),
                new("set-cookie", "b=2"),
            ],
            []);

        string value = ReferenceEvaluator.Evaluate(Reference("json.response.headers.Set-Cookie"), Exchange(response));

        Assert.Equal("a=1, b=2", value);
    }

    [Fact]
    public void Evaluate_MissingHeader_ThrowsNamingRequestAndHeader()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("json.response.headers.X-Missing"), JsonExchange()));

        Assert.Equal("'json' response has no 'X-Missing' header.", exception.Message);
    }

    [Fact]
    public void Evaluate_AllHeaders_ThrowsUnsupported()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("json.response.headers.*"), JsonExchange()));

        Assert.Equal(
            "'json.response.headers.*' is not supported. Name one header, as in 'headers.Content-Type'.",
            exception.Message);
    }

    [Fact]
    public void Evaluate_UnknownBodySelector_ThrowsUnsupported()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("json.response.body.str"), JsonExchange()));

        Assert.Equal(
            "'json.response.body.str' is not supported. After 'body.' use '*', a JSONPath starting with '$' "
            + "or an XPath starting with '/'.",
            exception.Message);
    }

    [Theory]
    [InlineData("/root/token")]
    [InlineData("//token")]
    public void Evaluate_XPathDocsForm_ReturnsValue(string selector)
    {
        string value = ReferenceEvaluator.Evaluate(Reference("xml.response.body." + selector), XmlExchange());

        Assert.Equal("xml-token-123", value);
    }

    [Fact]
    public void Evaluate_XPathOverNonXmlContentType_ReturnsValue()
    {
        HttpExchange exchange = Exchange(Response("text/plain", Encoding.UTF8.GetBytes(CaptureXml)));

        string value = ReferenceEvaluator.Evaluate(Reference("xml.response.body./root/token"), exchange);

        Assert.Equal("xml-token-123", value);
    }

    [Fact]
    public void Evaluate_XPathElementWithChildren_ReturnsStringValue()
    {
        string value = ReferenceEvaluator.Evaluate(Reference("xml.response.body./root"), XmlExchange());

        Assert.Equal("xml-token-1237", value);
    }

    [Fact]
    public void Evaluate_XPathAttribute_ReturnsValue()
    {
        string value = ReferenceEvaluator.Evaluate(
            Reference("xml.response.body./root/@id"),
            XmlExchange("""<root id="r1" />"""));

        Assert.Equal("r1", value);
    }

    [Theory]
    [InlineData("//n + 1", "8")]
    [InlineData("//n div 2", "3.5")]
    [InlineData("/root/n = 7", "true")]
    [InlineData("/root/n = 8", "false")]
    [InlineData("/root/token | /root/missing", "xml-token-123")]
    public void Evaluate_XPathScalarOrUnionResult_ReturnsInvariantText(string selector, string expected)
    {
        string value = ReferenceEvaluator.Evaluate(Reference("xml.response.body." + selector), XmlExchange());

        Assert.Equal(expected, value);
    }

    [Fact]
    public void Evaluate_XPathNoMatch_ThrowsVsMessage()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("xml.response.body./root/nope"), XmlExchange()));

        Assert.Equal(
            "The named request does not contain any content at this path 'xml.response.body./root/nope'.",
            exception.Message);
    }

    [Fact]
    public void Evaluate_XPathSeveralNodes_ThrowsWithCount()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("xml.response.body./root/*"), XmlExchange()));

        Assert.Equal("XPath '/root/*' matched 2 nodes; expected one.", exception.Message);
    }

    [Theory]
    [InlineData("/root/[")]
    [InlineData("/root/unknown-function()")]
    [InlineData("/a:root")]
    public void Evaluate_InvalidXPath_ThrowsNamingExpression(string selector)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("xml.response.body." + selector), XmlExchange()));

        Assert.Equal(
            $"Invalid XPath '{selector}' in 'xml.response.body.{selector}'. Namespace prefixes, variables "
            + "and XSLT functions are not supported.",
            exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"token": "secret-token"}""")]
    [InlineData("<root><token>secret-token</root>")]
    public void Evaluate_BodyNotXml_ThrowsWithoutBodyContent(string body)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("xml.response.body./root/token"), XmlExchange(body)));

        Assert.Equal(NotXmlMessage, exception.Message);
    }

    [Theory]
    [InlineData("""<!DOCTYPE root [<!ENTITY xxe SYSTEM "file:///c:/windows/win.ini">]><root><token>&xxe;</token></root>""")]
    [InlineData("""<!DOCTYPE root [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;">]><root><token>&b;</token></root>""")]
    [InlineData("""<!DOCTYPE root><root><token>abc</token></root>""")]
    public void Evaluate_XmlBodyWithDoctype_Throws(string body)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("xml.response.body./root/token"), XmlExchange(body)));

        Assert.Equal(NotXmlMessage, exception.Message);
    }

    [Fact]
    public void Evaluate_RequestBody_ReadsResolvedRequest()
    {
        HttpExchange exchange = JsonExchange() with { Request = Request("""{"user": "jane"}""") };

        string whole = ReferenceEvaluator.Evaluate(Reference("login.request.body.*"), exchange);
        string user = ReferenceEvaluator.Evaluate(Reference("login.request.body.$.user"), exchange);

        Assert.Equal("""{"user": "jane"}""", whole);
        Assert.Equal("jane", user);
    }

    [Fact]
    public void Evaluate_RequestBodyXPath_ReadsResolvedRequest()
    {
        HttpExchange exchange = JsonExchange() with { Request = Request(CaptureXml) };

        string value = ReferenceEvaluator.Evaluate(Reference("login.request.body.//token"), exchange);

        Assert.Equal("xml-token-123", value);
    }

    [Fact]
    public void Evaluate_NullRequestBody_ReturnsEmpty()
    {
        string value = ReferenceEvaluator.Evaluate(Reference("login.request.body.*"), JsonExchange());

        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public void Evaluate_NullRequestBodyJsonPath_ThrowsNotJson()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("login.request.body.$.user"), JsonExchange()));

        Assert.Equal("'login' request body is not JSON.", exception.Message);
    }

    [Fact]
    public void Evaluate_RequestHeader_ReadsResolvedRequest()
    {
        string value = ReferenceEvaluator.Evaluate(Reference("login.request.headers.x-trace"), JsonExchange());

        Assert.Equal("trace-1", value);
    }

    [Fact]
    public void Evaluate_MissingRequestHeader_ThrowsNamingRequestAndHeader()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference("login.request.headers.Accept"), JsonExchange()));

        Assert.Equal("'login' request has no 'Accept' header.", exception.Message);
    }

    private static RequestReferenceSegment Reference(string expression) =>
        Assert.IsType<RequestReferenceSegment>(Assert.Single(Template.Parse("{{" + expression + "}}").Segments));

    [Theory]
    [InlineData("json.response.body.*")]
    [InlineData("json.response.body.$.token")]
    public void Evaluate_ResponseBodyInACodingThatIsNotDecoded_ThrowsNamingTheCoding(string expression)
    {
        HttpExchange exchange = Exchange(
            Response([new(ContentTypeHeader, JsonContentType), new("Content-Encoding", "br")], [0x0B, 0x02, 0x80]));

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => ReferenceEvaluator.Evaluate(Reference(expression), exchange));

        Assert.Equal(
            "'json' response body is in Content-Encoding br, which is not decoded, so it cannot be referenced.",
            exception.Message);
    }

    [Fact]
    public void Evaluate_EmptyResponseBodyWithACodingThatIsNotDecoded_ReturnsEmptyText()
    {
        HttpExchange exchange = Exchange(Response([new("Content-Encoding", "br")], []));

        string value = ReferenceEvaluator.Evaluate(Reference("json.response.body.*"), exchange);

        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public void Evaluate_ResponseHeaderOfABodyInACodingThatIsNotDecoded_ReadsTheHeader()
    {
        HttpExchange exchange = Exchange(Response([new("Content-Encoding", "br")], [0x0B, 0x02, 0x80]));

        string value = ReferenceEvaluator.Evaluate(Reference("json.response.headers.Content-Encoding"), exchange);

        Assert.Equal("br", value);
    }

    private static HttpExchange JsonExchange(string body = CaptureJson) =>
        Exchange(Response(JsonContentType, Encoding.UTF8.GetBytes(body)));

    private static HttpExchange XmlExchange(string body = CaptureXml) =>
        Exchange(Response(XmlContentType, Encoding.UTF8.GetBytes(body)));

    private static HttpExchange Exchange(ReceivedResponse response) => new(Request(body: null), response);

    private static ResolvedRequest Request(string? body) =>
        new("POST", new Uri("http://localhost:5199/echo"), HttpVersion: null, [new("X-Trace", "trace-1")], body)
        {
            DisplayUrl = "http://localhost:5199/echo",
        };

    private static ReceivedResponse Response(string? contentType, byte[] body) =>
        Response(contentType is null ? [] : [new(ContentTypeHeader, contentType)], body);

    private static ReceivedResponse Response(IReadOnlyList<KeyValuePair<string, string>> headers, byte[] body) =>
        new(new Version(1, 1), 200, "OK", headers, body, TimeSpan.FromMilliseconds(12));
}
