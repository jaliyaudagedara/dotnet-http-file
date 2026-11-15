using DotNetHttpFile.Execution;

namespace DotNetHttpFile.Tests.Execution;

public sealed class GraphQLRequestTests
{
    private const int RequestLine = 3;
    private const int MarkerLine = 4;

    private static readonly Uri s_url = new("https://localhost:7128/graphql");

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
    [InlineData("{ a }\n\n{\"token\": \"s3cret\"")]
    [InlineData("{ a }\n\n[\"s3cret\"]")]
    [InlineData("{ a }\n\n\"s3cret\"")]
    [InlineData("{ a }\n\n{\"id\": 1, \"id\": \"s3cret\"}")]
    [InlineData("{ a }\n\n{\"id\": 1}\n\n{\"s3cret\": 2}")]
    public void Apply_VariablesNotAJsonObject_ThrowsWithoutQuotingThem(string body)
    {
        ResolvedRequest request = Marked(body);

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => GraphQLRequest.Apply(request, RequestLine));

        Assert.Equal(
            $"The GraphQL variables of the request on line {RequestLine} are not a JSON object; the request "
            + "cannot be sent.",
            exception.Message);
    }

    private static ResolvedRequest Marked(string? body, params (string Name, string Value, int Line)[] headers) =>
        ResolvedRequestFactory.Create(
            "POST",
            s_url,
            body,
            null,
            headers.Length == 0 ? [("X-Request-Type", "graphql", MarkerLine)] : headers);
}
