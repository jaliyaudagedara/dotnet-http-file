using DotNetHttpFile.Parsing;
using DotNetHttpFile.Templates;
using DotNetHttpFile.Tests.Support;

namespace DotNetHttpFile.Tests.Parsing;

public sealed class HttpFileParserTests
{
    private const string FilePath = "test.http";

    [Fact]
    public void Parse_ThreeRequestsWithSeparators_ReturnsThree()
    {
        string content = Crlf(
            "GET https://localhost:7220/weatherforecast",
            "",
            "###",
            "",
            "GET https://localhost:7220/weatherforecast?date=2023-05-11&location=98006",
            "",
            "###",
            "",
            "GET https://localhost:7220/weatherforecast HTTP/3",
            "",
            "###",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal(
            [
                ("GET", "https://localhost:7220/weatherforecast", null, 1),
                ("GET", "https://localhost:7220/weatherforecast?date=2023-05-11&location=98006", null, 5),
                ("GET", "https://localhost:7220/weatherforecast", "HTTP/3", 9),
            ],
            file.Requests.Select(request => (request.Method, request.Url.Raw, request.HttpVersion, request.Line)));
    }

    [Fact]
    public void Parse_AnyContent_KeepsPath()
    {
        HttpFile file = HttpFileParser.Parse(FilePath, "GET https://localhost/x");

        Assert.Equal(FilePath, file.Path);
    }

    [Fact]
    public void Parse_NameComment_NamesFollowingRequest()
    {
        string content = Crlf(
            "# @name login",
            "",
            "POST https://localhost/login",
            "",
            "###",
            "// @name list",
            "",
            "GET https://localhost/items",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal(["login", "list"], file.Requests.Select(request => request.Name));
    }

    [Fact]
    public void Parse_NameCommentBeforeSeparator_DoesNotNameLaterRequest()
    {
        string content = Crlf(
            "# @name orphan",
            "###",
            "GET https://localhost/items",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Null(Assert.Single(file.Requests).Name);
    }

    [Fact]
    public void Parse_SeparatorWithText_SetsTitleOfNextRequest()
    {
        string content = Crlf(
            "GET https://a/1",
            "",
            "### List existing indexes",
            "GET https://a/2",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal([null, "List existing indexes"], file.Requests.Select(request => request.Title));
    }

    [Theory]
    [InlineData("###")]
    [InlineData("###   ")]
    public void Parse_BareSeparator_LeavesTitleNull(string separator)
    {
        string content = Crlf(separator, "GET https://a/2", "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Null(Assert.Single(file.Requests).Title);
    }

    [Fact]
    public void Parse_SeparatorTitle_IsTrimmed()
    {
        string content = Crlf("###   Get one  ", "GET https://a/1", "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal("Get one", Assert.Single(file.Requests).Title);
    }

    [Fact]
    public void Parse_UrlWithoutMethod_DefaultsToGet()
    {
        HttpFile file = HttpFileParser.Parse(FilePath, "https://localhost/items\r\n");

        RequestDefinition request = Assert.Single(file.Requests);
        Assert.Equal(("GET", "https://localhost/items"), (request.Method, request.Url.Raw));
    }

    [Theory]
    [InlineData("OPTIONS")]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("TRACE")]
    [InlineData("CONNECT")]
    public void Parse_DocumentedMethod_ReturnsMethodAndUrl(string method)
    {
        HttpFile file = HttpFileParser.Parse(FilePath, method + " https://localhost/items HTTP/1.1\r\n");

        RequestDefinition request = Assert.Single(file.Requests);
        Assert.Equal(
            (method, "https://localhost/items", "HTTP/1.1"),
            (request.Method, request.Url.Raw, request.HttpVersion));
    }

    [Fact]
    public void Parse_HeadersAndBody_ReturnsHeadersAndBody()
    {
        string content = Crlf(
            "POST https://localhost/items",
            "Content-Type: application/json",
            "Authorization:Bearer {{token}}",
            "X-Time: 12:30",
            "",
            "{\"id\": \"{{id}}\"}",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        RequestDefinition request = Assert.Single(file.Requests);
        Assert.Equal(
            [
                ("Content-Type", "application/json", 2),
                ("Authorization", "Bearer {{token}}", 3),
                ("X-Time", "12:30", 4),
            ],
            request.Headers.Select(header => (header.Name, header.Value.Raw, header.Line)));
        Assert.Equal(
            [
                new LiteralSegment("{\"id\": \""),
                new VariableSegment("id"),
                new LiteralSegment("\"}"),
            ],
            request.Body?.Segments);
    }

    [Fact]
    public void Parse_RequestWithoutBody_ReturnsNullBody()
    {
        string content = Crlf(
            "GET https://localhost/items",
            "Accept: application/json",
            "",
            "",
            "###",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Null(Assert.Single(file.Requests).Body);
    }

    [Fact]
    public void Parse_TrailingBlankLinesBeforeSeparator_AreTrimmed()
    {
        string content = "POST https://localhost/echo?case=2\r\n\r\nline1\r\nline2\r\n\r\n\r\n\r\n###\r\n";

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal("line1\r\nline2", Assert.Single(file.Requests).Body?.Raw);
    }

    [Fact]
    public void Parse_SeparatorRightAfterBody_KeepsBodyExact()
    {
        string content = Crlf(
            "POST https://localhost/echo?case=3",
            "Content-Type: application/json",
            "",
            "{\"a\": 1}",
            "###",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal("{\"a\": 1}", Assert.Single(file.Requests).Body?.Raw);
    }

    [Fact]
    public void Parse_Utf8BomAndLfEndings_ParsesRequest()
    {
        string content = "\uFEFF@host=https://localhost\n# @name items\nPOST {{host}}/items\nAccept: */*\n\na\nb\n";

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        RequestDefinition request = Assert.Single(file.Requests);
        Assert.Equal(
            ("items", "POST", "{{host}}/items", "a\nb", 3),
            (request.Name, request.Method, request.Url.Raw, request.Body?.Raw, request.Line));
        Assert.Equal("host", Assert.Single(file.Variables).Name);
    }

    [Fact]
    public void Parse_BodyAfterHeadersAndBlankLines_RecordsFirstBodyLine()
    {
        string content = Crlf(
            "POST https://localhost/items",
            "Accept: */*",
            "",
            "",
            "{",
            "}",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal(5, Assert.Single(file.Requests).BodyLine);
    }

    [Fact]
    public void Parse_RequestWithoutBody_HasNoBodyLine()
    {
        HttpFile file = HttpFileParser.Parse(FilePath, Crlf("GET https://localhost/items", ""));

        Assert.Null(Assert.Single(file.Requests).BodyLine);
    }

    [Fact]
    public void Parse_MixedLineEndings_PreservesBodyEndings()
    {
        string content = "POST https://localhost/items\r\n\na\nb\r\nc\r###\n";

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal("a\nb\r\nc", Assert.Single(file.Requests).Body?.Raw);
    }

    [Fact]
    public void Parse_NoFinalNewline_ParsesRequest()
    {
        string content = "POST https://localhost/items\r\nAccept: */*\r\n\r\nbody";

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        RequestDefinition request = Assert.Single(file.Requests);
        Assert.Equal(("*/*", "body"), (Assert.Single(request.Headers).Value.Raw, request.Body?.Raw));
    }

    [Fact]
    public void Parse_CommentLikeLinesInBody_AreKept()
    {
        string content = Crlf(
            "POST https://localhost/items",
            "",
            "# heading",
            "// note",
            "@user=me",
            "###",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal("# heading\r\n// note\r\n@user=me", Assert.Single(file.Requests).Body?.Raw);
    }

    [Fact]
    public void Parse_UnclosedBracesInBodyLine_KeepsLaterExpressions()
    {
        string content = Crlf(
            "POST https://localhost/items",
            "",
            "open {{ here",
            "id={{id}}",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal(
            [
                new LiteralSegment("open {{ here\r\nid="),
                new VariableSegment("id"),
            ],
            Assert.Single(file.Requests).Body?.Segments);
    }

    [Fact]
    public void Parse_VariableWithSpacesAroundEquals_TrimsNameAndValue()
    {
        HttpFile file = HttpFileParser.Parse(FilePath, "@host = https://localhost/?a=b  \r\n");

        FileVariable variable = Assert.Single(file.Variables);
        Assert.Equal(("host", "https://localhost/?a=b", 1), (variable.Name, variable.Value.Raw, variable.Line));
    }

    [Fact]
    public void Parse_VariablesAfterRequest_NotVisibleToIt()
    {
        string content = Crlf(
            "@before=1",
            "GET https://localhost/{{before}}",
            "",
            "###",
            "@after=2",
            "GET https://localhost/{{after}}",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal(["before"], file.VariablesVisibleAt(file.Requests[0].Line).Keys);
        Assert.Equal(["after", "before"], file.VariablesVisibleAt(file.Requests[1].Line).Keys.Order());
    }

    [Fact]
    public void Parse_VariableRedefined_LaterDefinitionWins()
    {
        string content = Crlf(
            "@host=first",
            "GET https://{{host}}/",
            "",
            "###",
            "@host=second",
            "GET https://{{host}}/",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal(
            ["first", "second"],
            file.Requests.Select(request => file.VariablesVisibleAt(request.Line)["host"].Value.Raw));
    }

    [Fact]
    public void Parse_CommentLines_Ignored()
    {
        string content = Crlf(
            "# a comment",
            "// another comment",
            "GET https://localhost/items",
            "# between the request line and a header",
            "Accept: */*",
            "// after the headers",
            "",
            "###",
            "# trailing comment",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        RequestDefinition request = Assert.Single(file.Requests);
        Assert.Equal(
            (null, "https://localhost/items", "Accept", null),
            (request.Name, request.Url.Raw, Assert.Single(request.Headers).Name, request.Body));
    }

    [Fact]
    public void Parse_DuplicateNames_Throws()
    {
        string content = Crlf(
            "# @name login",
            "GET https://localhost/a",
            "",
            "###",
            "// @name login",
            "GET https://localhost/b",
            "");

        HttpFileException exception = Assert.Throws<HttpFileException>(() => HttpFileParser.Parse(FilePath, content));

        Assert.Equal("Duplicate request name 'login' on lines 2 and 6.", exception.Message);
    }

    [Fact]
    public void Parse_InvalidExpressionInHeader_ThrowsWithHeaderLine()
    {
        string content = Crlf(
            "GET https://localhost/items",
            "Accept: */*",
            "X-Bad: {{a b}}",
            "");

        HttpFileException exception = Assert.Throws<HttpFileException>(() => HttpFileParser.Parse(FilePath, content));

        Assert.Equal("Invalid expression '{{a b}}' (line 3).", exception.Message);
        HttpFileException inner = Assert.IsType<HttpFileException>(exception.InnerException);
        Assert.Equal("Invalid expression '{{a b}}'.", inner.Message);
    }

    [Theory]
    [InlineData("@bad={{a b}}\r\nGET https://localhost/\r\n", 1)]
    [InlineData("\r\nGET https://localhost/{{a b}}\r\n", 2)]
    [InlineData("POST https://localhost/\r\nAccept: */*\r\n\r\nfine\r\n{{a b}}\r\n", 5)]
    [InlineData("POST https://localhost/\n\n\nfine\rmore\r\n\r\n{{a b}}", 7)]
    public void Parse_InvalidExpression_ThrowsWithExpressionLine(string content, int line)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(() => HttpFileParser.Parse(FilePath, content));

        Assert.Equal($"Invalid expression '{{{{a b}}}}' (line {line}).", exception.Message);
    }

    [Theory]
    [InlineData("not a header", "not")]
    [InlineData(": value", "")]
    [InlineData("Bad Name: value", "Bad")]
    public void Parse_MalformedHeader_Throws(string header, string echoed)
    {
        string content = Crlf("GET https://localhost/items", header, "");

        HttpFileException exception = Assert.Throws<HttpFileException>(() => HttpFileParser.Parse(FilePath, content));

        Assert.Equal($"Invalid header '{echoed}' (line 2).", exception.Message);
    }

    [Fact]
    public void Parse_MalformedHeaderWithToken_DoesNotEchoToken()
    {
        string content = Crlf(
            "GET https://localhost/items",
            "Authorization Bearer fake-token-123",
            "");

        HttpFileException exception = Assert.Throws<HttpFileException>(() => HttpFileParser.Parse(FilePath, content));

        Assert.Equal("Invalid header 'Authorization' (line 2).", exception.Message);
        Assert.DoesNotContain("fake-token-123", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("@host", "@host")]
    [InlineData("@=value", "@")]
    [InlineData("@my host=value", "@my")]
    public void Parse_MalformedVariable_Throws(string line, string echoed)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => HttpFileParser.Parse(FilePath, "\r\n" + line + "\r\n"));

        Assert.Equal($"Invalid variable definition '{echoed}' (line 2).", exception.Message);
    }

    [Fact]
    public void Parse_MalformedVariableWithToken_DoesNotEchoToken()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => HttpFileParser.Parse(FilePath, "@token fake-token-123\r\n"));

        Assert.Equal("Invalid variable definition '@token' (line 1).", exception.Message);
        Assert.DoesNotContain("fake-token-123", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CommentAfterBodilessRequest_ReturnsNullBody()
    {
        string content = Crlf(
            "GET https://localhost/items",
            "",
            "# a comment",
            "",
            "// another comment",
            "###",
            "");

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Null(Assert.Single(file.Requests).Body);
    }

    [Fact]
    public void Parse_LeadingBlankLinesInBody_AreTrimmed()
    {
        string content = "POST https://localhost/items\r\n\r\n\r\n \r\nline1\n\nline2\r\n\r\n###\r\n";

        HttpFile file = HttpFileParser.Parse(FilePath, content);

        Assert.Equal("line1\n\nline2", Assert.Single(file.Requests).Body?.Raw);
    }

    [Theory]
    [InlineData("get https://localhost/items", "get")]
    [InlineData("FOO https://localhost/items", "FOO")]
    [InlineData("FOO https://localhost/items HTTP/2", "FOO")]
    public void Parse_UnknownMethod_Throws(string requestLine, string method)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => HttpFileParser.Parse(FilePath, "\r\n" + requestLine + "\r\n"));

        Assert.Equal($"Unknown HTTP method '{method}' (line 2).", exception.Message);
    }

    [Fact]
    public void Parse_UrlWithoutMethodWithSpacesInExpression_DefaultsToGet()
    {
        HttpFile file = HttpFileParser.Parse(FilePath, "https://localhost/{{$randomInt 1 9}} HTTP/2\r\n");

        RequestDefinition request = Assert.Single(file.Requests);
        Assert.Equal(
            ("GET", "https://localhost/{{$randomInt 1 9}}", "HTTP/2"),
            (request.Method, request.Url.Raw, request.HttpVersion));
    }

    [Theory]
    [InlineData("# @name", "")]
    [InlineData("// @name   ", "")]
    [InlineData("# @name first second", "first")]
    public void Parse_MalformedNameComment_Throws(string comment, string echoed)
    {
        string content = Crlf("", comment, "GET https://localhost/items", "");

        HttpFileException exception = Assert.Throws<HttpFileException>(() => HttpFileParser.Parse(FilePath, content));

        Assert.Equal($"Invalid request name '{echoed}' (line 2).", exception.Message);
    }

    [Fact]
    public void Parse_UrlWithoutMethodWithLiteralSpace_EchoesOnlyLeadingToken()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => HttpFileParser.Parse(FilePath, "https://api/x?key=fake-key-123&q=a b\r\n"));

        Assert.Equal("Unknown HTTP method 'https' (line 1).", exception.Message);
        Assert.DoesNotContain("fake-key-123", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://host/{{oops more")]
    [InlineData("https://host/{{oops more}")]
    [InlineData("https://host/{{a {{b}}")]
    [InlineData(@"https://host/\{{a b}}")]
    public void Parse_WhiteSpaceAfterUnclosedExpression_ThrowsUnknownMethod(string requestLine)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => HttpFileParser.Parse(FilePath, requestLine + "\r\n"));

        Assert.Equal("Unknown HTTP method 'https' (line 1).", exception.Message);
    }

    [Fact]
    public void Parse_UnclosedExpressionWithoutWhiteSpace_IsLiteralUrlText()
    {
        HttpFile file = HttpFileParser.Parse(FilePath, "https://host/{{oops/{{$randomInt 1 9}}\r\n");

        RequestDefinition request = Assert.Single(file.Requests);
        Assert.Equal(("GET", "https://host/{{oops/{{$randomInt 1 9}}"), (request.Method, request.Url.Raw));
    }

    [Fact]
    public void Parse_MethodFollowedByVersionOnly_ReadsMethodAsUrl()
    {
        HttpFile file = HttpFileParser.Parse(FilePath, "GET HTTP/1.1\r\n");

        RequestDefinition request = Assert.Single(file.Requests);
        Assert.Equal(("GET", "GET", "HTTP/1.1"), (request.Method, request.Url.Raw, request.HttpVersion));
    }

    [Fact]
    public void Parse_ExpressionAsMethod_ThrowsUnknownMethod()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => HttpFileParser.Parse(FilePath, "{{method}} https://localhost/items\r\n"));

        Assert.Equal("Unknown HTTP method '{{method}}' (line 1).", exception.Message);
    }

    [Theory]
    [InlineData("GET https://localhost/items\r\n{0}\r\n", "Invalid header '{0}' (line 2).")]
    [InlineData("\r\n@{0}\r\n", "Invalid variable definition '@{0}' (line 2).")]
    [InlineData("\r\n{0} https://localhost/items\r\n", "Unknown HTTP method '{0}' (line 2).")]
    [InlineData("\r\n# @name {0} second\r\nGET https://localhost/items\r\n", "Invalid request name '{0}' (line 2).")]
    public void Parse_MalformedLineWithLongToken_EchoesAtMost32Characters(string contentFormat, string messageFormat)
    {
        // The variable line starts with '@', which counts towards the 32 characters.
        bool hasPrefix = messageFormat.Contains("@{0}", StringComparison.Ordinal);
        string token = "fake.token." + new string('x', 40);
        string content = contentFormat.Replace("{0}", token, StringComparison.Ordinal);

        HttpFileException exception = Assert.Throws<HttpFileException>(() => HttpFileParser.Parse(FilePath, content));

        string echoed = token[..(hasPrefix ? 31 : 32)] + "...";
        Assert.Equal(messageFormat.Replace("{0}", echoed, StringComparison.Ordinal), exception.Message);
    }

    [Fact]
    public void Parse_MalformedLineWith32CharacterToken_EchoesItWhole()
    {
        string token = new('x', 32);
        string content = Crlf("GET https://localhost/items", token, "");

        HttpFileException exception = Assert.Throws<HttpFileException>(() => HttpFileParser.Parse(FilePath, content));

        Assert.Equal($"Invalid header '{token}' (line 2).", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_CaptureFile_MatchesStructure()
    {
        string path = VisualStudioCapture.CaptureFilePath;

        HttpFile file = await HttpFileParser.LoadAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["json", null, null, null, null, null, null, null, "xml", null, null, null],
            file.Requests.Select(request => request.Name));
    }

    [Fact]
    public void Parse_CaptureFile_MatchesStructure()
    {
        string path = VisualStudioCapture.CaptureFilePath;

        HttpFile file = HttpFileParser.Parse(path, File.ReadAllText(path));

        Assert.Equal(12, file.Requests.Count);
        Assert.Equal(("json", "xml"), (file.Requests[0].Name, file.Requests[8].Name));
        Assert.Equal(
            ("line1\r\nline2", "{\"a\": 1}"),
            (file.Requests[1].Body?.Raw, file.Requests[2].Body?.Raw));
    }

    private static string Crlf(params string[] lines) => string.Join("\r\n", lines);
}
