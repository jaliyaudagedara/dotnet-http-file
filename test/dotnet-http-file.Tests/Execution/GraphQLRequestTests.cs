using System.Text.Encodings.Web;
using System.Text.Json;
using DotNetHttpFile.Execution;

namespace DotNetHttpFile.Tests.Execution;

public sealed class GraphQLRequestTests
{
    private const int RequestLine = 3;
    private const int MarkerLine = 4;

    private static readonly Uri s_url = new("https://localhost:7128/graphql");

    private static readonly JsonSerializerOptions s_relaxedJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public void Apply_NoMarker_ReturnsTheRequestUnchanged()
    {
        ResolvedRequest request = ResolvedRequestFactory.Create(
            "POST",
            s_url,
            "{ hero { name } }",
            null,
            ("X-Request-Type", "rest", MarkerLine));

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Same(request, applied);
    }

    [Theory]
    [InlineData("X-Request-Type", "graphql")]
    [InlineData("x-request-type", "GraphQL")]
    [InlineData("X-REQUEST-TYPE", "  graphql  ")]
    public void Apply_Marker_LeavesTheMarkerHeaderOut(string name, string value)
    {
        ResolvedRequest request = Marked("{ hero { name } }", (name, value, MarkerLine));

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.DoesNotContain(
            applied.Headers,
            header => string.Equals(header.Key, "X-Request-Type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_AnonymousQuery_SendsQueryOnly()
    {
        ResolvedRequest request = Marked("  { hero { name } }  ");

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal("""{"query":"{ hero { name } }"}""", applied.Body);
    }

    [Fact]
    public void Apply_QueryAndVariables_SendsCompactEnvelope()
    {
        ResolvedRequest request = Marked(
            "query Hero($id: ID!) {\r\n  hero(id: $id) { name }\r\n}\r\n\r\n{\r\n  \"id\": \"1\",\r\n  \"n\": [1, 2]\r\n}");

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal(
            """{"query":"query Hero($id: ID!) {\r\n  hero(id: $id) { name }\r\n}","variables":{"id":"1","n":[1,2]},"operationName":"Hero"}""",
            applied.Body);
    }

    [Theory]
    [InlineData("query\n{ a }\n\n{}\n", "\n")]
    [InlineData("query\r{ a }\r\r{}\r", "\r")]
    [InlineData("query\n{ a }\n   \t\n{}", "\n")]
    public void Apply_BlankLineInAnyLineEnding_SplitsThere(string body, string lineBreak)
    {
        ResolvedRequest request = Marked(body);

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        string escapedLineBreak = lineBreak == "\n" ? "\\n" : "\\r";
        Assert.Equal($$$"""{"query":"query{{{escapedLineBreak}}}{ a }","variables":{}}""", applied.Body);
    }

    [Theory]
    [MemberData(nameof(LineBreaks))]
    public void Apply_BlankLineBeforeFragmentAndNoVariables_SendsTheWholeBodyAsTheQuery(string lineBreak)
    {
        string query = string.Join(
            lineBreak,
            "query Hero {",
            "  hero { ...F }",
            "}",
            "",
            "fragment F on Hero {",
            "  name",
            "}");
        ResolvedRequest request = Marked(query + lineBreak);

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal(
            JsonSerializer.Serialize(new { query, operationName = "Hero" }, s_relaxedJson),
            applied.Body);
    }

    [Theory]
    [MemberData(nameof(LineBreaks))]
    public void Apply_BlankLineBeforeFragmentThenVariables_SplitsAtTheLastBlankLine(string lineBreak)
    {
        string query = string.Join(
            lineBreak,
            "query Hero($id: ID!) {",
            "  hero(id: $id) { ...F }",
            "}",
            "",
            "fragment F on Hero {",
            "  name",
            "}");
        ResolvedRequest request = Marked(string.Join(lineBreak, query, "", "{", "  \"id\": \"1\"", "}", ""));

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal(
            JsonSerializer.Serialize(
                new
                {
                    query,
                    variables = new { id = "1" },
                    operationName = "Hero",
                },
                s_relaxedJson),
            applied.Body);
    }

    [Fact]
    public void Apply_JsonObjectsInTheLastTwoBlocks_TakesOnlyTheLastAsVariables()
    {
        ResolvedRequest request = Marked("{ a }\n\n{\"id\": 1}\n\n{\"id\": 2}");

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal("""{"query":"{ a }\n\n{\"id\": 1}","variables":{"id":2}}""", applied.Body);
    }

    [Theory]
    [InlineData("{ a }\n\n\n{}")]
    [InlineData("{ a }\n \n\t\n{}")]
    [InlineData("{ a }\r\n\r\n\r\n{}\r\n")]
    public void Apply_SeveralBlankLinesBeforeVariables_LeavesThemOutOfTheQuery(string body)
    {
        ResolvedRequest request = Marked(body);

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal("""{"query":"{ a }","variables":{}}""", applied.Body);
    }

    [Theory]
    [MemberData(nameof(LineBreaks))]
    public void Apply_BlankLineInsideTheVariables_TakesTheWholeObject(string lineBreak)
    {
        ResolvedRequest request = Marked(
            string.Join(lineBreak, "{ a }", "", "{", "  \"a\": 1,", "", "  \"b\": 2", "}"));

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal("""{"query":"{ a }","variables":{"a":1,"b":2}}""", applied.Body);
    }

    [Theory]
    [MemberData(nameof(LineBreaks))]
    public void Apply_BlankLinesAfterTheVariables_AreIgnored(string lineBreak)
    {
        ResolvedRequest request = Marked(
            string.Join(lineBreak, "{ a }  ", "", "{\"id\": 1}", "", "  ", "", ""));

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal("""{"query":"{ a }","variables":{"id":1}}""", applied.Body);
    }

    [Fact]
    public void Apply_WhitespaceAfterBlankLine_SendsNoVariables()
    {
        ResolvedRequest request = Marked("{ a }\n\n   \n");

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal("""{"query":"{ a }"}""", applied.Body);
    }

    [Theory]
    [InlineData("query Hero { a }", "Hero")]
    [InlineData("mutation AddHero($n: String) { a }", "AddHero")]
    [InlineData("subscription OnHero{ a }", "OnHero")]
    [InlineData("# a comment\n  # another\n\tquery _Named1 { a }", "_Named1")]
    public void Apply_NamedOperation_SetsOperationName(string query, string expected)
    {
        ResolvedRequest request = Marked(query);

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.EndsWith($"\"operationName\":\"{expected}\"}}", applied.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ a }")]
    [InlineData("query { a }")]
    [InlineData("query($id: ID) { a }")]
    [InlineData("fragment F on Hero { a }")]
    [InlineData("# query Hero\n{ a }")]
    public void Apply_NoNamedOperation_LeavesOperationNameOut(string query)
    {
        ResolvedRequest request = Marked(query);

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.DoesNotContain("operationName", applied.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_QuotesAndNonAscii_EscapesOnlyWhatJsonRequires()
    {
        ResolvedRequest request = Marked("{ hero(name: \"Ünïcode <&>\") { a } }");

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal("""{"query":"{ hero(name: \"Ünïcode <&>\") { a } }"}""", applied.Body);
    }

    [Fact]
    public void Apply_NoContentTypeWritten_SendsApplicationJsonOnTheMarkerLine()
    {
        ResolvedRequest request = Marked("{ a }", ("Accept", "*/*", 5), ("X-Request-Type", "graphql", MarkerLine));

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal(
            [KeyValuePair.Create("Accept", "*/*"), KeyValuePair.Create("Content-Type", "application/json")],
            applied.Headers);
        Assert.Equal([5, MarkerLine], applied.HeaderLines);
    }

    [Fact]
    public void Apply_ContentTypeWritten_SendsItAsWritten()
    {
        ResolvedRequest request = Marked(
            "{ a }",
            ("X-Request-Type", "graphql", MarkerLine),
            ("content-type", "application/graphql+json", 5));

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal([KeyValuePair.Create("content-type", "application/graphql+json")], applied.Headers);
        Assert.Equal([5], applied.HeaderLines);
    }

    [Fact]
    public void Apply_Marker_KeepsMethodUrlAndDisplayUrl()
    {
        ResolvedRequest request = Marked("{ a }") with { DisplayUrl = "https://***/graphql" };

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal(
            ("POST", s_url, "https://***/graphql"),
            (applied.Method, applied.Url, applied.DisplayUrl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("\n\n{\"id\": 1}")]
    public void Apply_NoQuery_Throws(string? body)
    {
        ResolvedRequest request = Marked(body);

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => GraphQLRequest.Apply(request, RequestLine));

        Assert.Equal(
            $"The GraphQL request on line {RequestLine} has no query and cannot be sent.",
            exception.Message);
    }

    [Theory]
    [InlineData("{ a }\n\n{\"id\": 1, \"id\": \"s3cret\"}")]
    [InlineData("{ a }\n\n{\"token\": \"s3cret\",}")]
    [InlineData("{ a }\n\n{\"token\": s3cret}\n")]
    [InlineData("{ a }\n\n{\"brace\": \"}\", \"quote\": \"\\\"}\", \"token\": s3cret}")]
    [InlineData("{ a }\n\n{ \"id\": 1, }")]
    [InlineData("{ a }\n\n{\"token\": \"s3cret\"")]
    [InlineData("{ a }\n\n{\"id\": 1} {\"token\": \"s3cret\"}")]
    [InlineData("{ a }\r\n\r\n  {\r\n  \"token\": s3cret\r\n}")]
    [InlineData("{ a }\n\n{ } s3cret")]
    public void Apply_LastBlockStartsLikeAJsonObjectButIsNotOne_ThrowsWithoutQuotingIt(string body)
    {
        ResolvedRequest request = Marked(body);

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => GraphQLRequest.Apply(request, RequestLine));

        Assert.Equal(
            $"The GraphQL variables of the request on line {RequestLine} are not a JSON object; the request "
            + "cannot be sent.",
            exception.Message);
    }

    [Theory]
    [InlineData("fragment F on Hero { name }\n\n{ hero { ...F } }")]
    [InlineData("fragment F on Hero { name }\r\n\r\n{\r\n  hero { ...F }\r\n}")]
    [InlineData("# The hero of the film\n\n{ hero { name } }")]
    [InlineData("fragment F on Query { a }\n\n{...F}")]
    [InlineData("{ a }\n\n{ b } { c }")]
    [InlineData("{ a }\n\n{ b }}")]
    [InlineData("{ a }\n\n{")]
    [InlineData("{ a }\n\n[\"id\"]")]
    [InlineData("{ a }\n\n\"id\"")]
    public void Apply_LastBlockDoesNotStartLikeAJsonObject_SendsTheWholeBodyAsTheQuery(string body)
    {
        ResolvedRequest request = Marked(body);

        ResolvedRequest applied = GraphQLRequest.Apply(request, RequestLine);

        Assert.Equal(JsonSerializer.Serialize(new { query = body }, s_relaxedJson), applied.Body);
    }

    public static TheoryData<string> LineBreaks() => ["\n", "\r", "\r\n"];

    private static ResolvedRequest Marked(string? body, params (string Name, string Value, int Line)[] headers) =>
        ResolvedRequestFactory.Create(
            "POST",
            s_url,
            body,
            null,
            headers.Length == 0 ? [("X-Request-Type", "graphql", MarkerLine)] : headers);
}
