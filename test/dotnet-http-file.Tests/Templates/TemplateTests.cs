using DotNetHttpFile.Templates;

namespace DotNetHttpFile.Tests.Templates;

public sealed class TemplateTests
{
    [Fact]
    public void Parse_PlainText_ReturnsSingleLiteral()
    {
        Template template = Template.Parse("https://localhost/x");

        Assert.Equal([new LiteralSegment("https://localhost/x")], template.Segments);
    }

    [Fact]
    public void Parse_AnyText_KeepsRaw()
    {
        Template template = Template.Parse("https://{{host}}/x");

        Assert.Equal("https://{{host}}/x", template.Raw);
    }

    [Fact]
    public void Parse_EmptyText_ReturnsNoSegments()
    {
        Template template = Template.Parse(string.Empty);

        Assert.Empty(template.Segments);
    }

    [Fact]
    public void Empty_Always_HasEmptyRawAndNoSegments()
    {
        Template template = Template.Empty;

        Assert.Equal((string.Empty, 0), (template.Raw, template.Segments.Count));
    }

    [Fact]
    public void Parse_Variable_ReturnsVariableSegment()
    {
        Template template = Template.Parse("https://{{host}}/x");

        Assert.Equal(
            [
                new LiteralSegment("https://"),
                new VariableSegment("host"),
                new LiteralSegment("/x"),
            ],
            template.Segments);
    }

    [Fact]
    public void Parse_VariableWithPaddingAndDots_TrimsName()
    {
        Template template = Template.Parse("{{ api.host }}");

        Assert.Equal([new VariableSegment("api.host")], template.Segments);
    }

    [Fact]
    public void Parse_AdjacentExpressions_ReturnsNoEmptyLiterals()
    {
        Template template = Template.Parse("{{scheme}}{{host}}");

        Assert.Equal([new VariableSegment("scheme"), new VariableSegment("host")], template.Segments);
    }

    [Fact]
    public void Parse_DynamicWithQuotedFormat_SplitsArguments()
    {
        Template template = Template.Parse("{{$datetime \"dd-MM-yyyy\" -1 y}}");

        DynamicSegment segment = Assert.IsType<DynamicSegment>(Assert.Single(template.Segments));
        Assert.Equal("datetime", segment.Function);
        Assert.Equal(["dd-MM-yyyy", "-1", "y"], segment.Arguments);
    }

    [Fact]
    public void Parse_DynamicWithoutArguments_ReturnsEmptyArguments()
    {
        Template template = Template.Parse("{{$randomInt}}");

        DynamicSegment segment = Assert.IsType<DynamicSegment>(Assert.Single(template.Segments));
        Assert.Equal("randomInt", segment.Function);
        Assert.Empty(segment.Arguments);
    }

    [Fact]
    public void Parse_DynamicWithQuotedSpaces_KeepsQuotedRunAsOneArgument()
    {
        Template template = Template.Parse("{{$datetime \"yyyy MM dd\"}}");

        DynamicSegment segment = Assert.IsType<DynamicSegment>(Assert.Single(template.Segments));
        Assert.Equal(["yyyy MM dd"], segment.Arguments);
    }

    [Theory]
    [InlineData("{{$}}")]
    [InlineData("{{$ datetime}}")]
    [InlineData("{{$datetime \"dd-MM}}")]
    public void Parse_MalformedDynamic_Throws(string text)
    {
        Assert.Throws<HttpFileException>(() => Template.Parse(text));
    }

    [Fact]
    public void Parse_ResponseBodyJsonPath_ReturnsReference()
    {
        Template template = Template.Parse("{{login.response.body.$.token}}");

        Assert.Equal(
            [
                new RequestReferenceSegment(
                    "login",
                    ReferenceSource.Response,
                    ReferencePart.Body,
                    "$.token",
                    "login.response.body.$.token"),
            ],
            template.Segments);
    }

    [Fact]
    public void Parse_ResponseHeader_ReturnsReference()
    {
        Template template = Template.Parse("{{x.response.headers.Content-Type}}");

        Assert.Equal(
            [
                new RequestReferenceSegment(
                    "x",
                    ReferenceSource.Response,
                    ReferencePart.Headers,
                    "Content-Type",
                    "x.response.headers.Content-Type"),
            ],
            template.Segments);
    }

    [Fact]
    public void Parse_RequestBody_ReturnsRequestSource()
    {
        Template template = Template.Parse("{{login.request.body.$.user}}");

        RequestReferenceSegment segment = Assert.IsType<RequestReferenceSegment>(Assert.Single(template.Segments));
        Assert.Equal(ReferenceSource.Request, segment.Source);
    }

    [Fact]
    public void Parse_XPathSelector_KeepsSlashes()
    {
        Template template = Template.Parse("{{xml.response.body.//token}}");

        RequestReferenceSegment segment = Assert.IsType<RequestReferenceSegment>(Assert.Single(template.Segments));
        Assert.Equal("//token", segment.Selector);
    }

    [Fact]
    public void Parse_SelectorWithSpaces_ReturnsReference()
    {
        Template template = Template.Parse("{{list.response.body.$.items[?(@.name == 'a b')].id}}");

        RequestReferenceSegment segment = Assert.IsType<RequestReferenceSegment>(Assert.Single(template.Segments));
        Assert.Equal("$.items[?(@.name == 'a b')].id", segment.Selector);
    }

    [Fact]
    public void Parse_ExpressionWithSpaces_Throws()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(() => Template.Parse("x {{a b}} y"));

        Assert.Equal("Invalid expression '{{a b}}'.", exception.Message);
    }

    [Theory]
    [InlineData("{{}}")]
    [InlineData("{{  }}")]
    public void Parse_EmptyExpression_Throws(string text)
    {
        Assert.Throws<HttpFileException>(() => Template.Parse(text));
    }

    [Fact]
    public void Parse_UnclosedBraces_IsLiteral()
    {
        Template template = Template.Parse("a {{b");

        Assert.Equal([new LiteralSegment("a {{b")], template.Segments);
    }

    [Fact]
    public void Parse_UnclosedBracesAfterExpression_KeepsTailAsOneLiteral()
    {
        Template template = Template.Parse("{{a}} b {{c");

        Assert.Equal([new VariableSegment("a"), new LiteralSegment(" b {{c")], template.Segments);
    }

    [Fact]
    public void Parse_OpeningBracesInsideExpression_AnchorsToLastOpening()
    {
        Template template = Template.Parse("{{a {{b}}");

        Assert.Equal([new LiteralSegment("{{a "), new VariableSegment("b")], template.Segments);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Parse_UnclosedBracesBeforeLineTerminator_IsLiteral(string terminator)
    {
        string text = "a {{b" + terminator + "}} {{c}}";

        Template template = Template.Parse(text);

        Assert.Equal(
            [
                new LiteralSegment("a {{b" + terminator + "}} "),
                new VariableSegment("c"),
            ],
            template.Segments);
    }

    [Theory]
    [InlineData("{{$fn a\"b c\"}}")]
    [InlineData("{{$fn \"a\"b}}")]
    public void Parse_DynamicWithMisplacedQuote_Throws(string text)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(() => Template.Parse(text));

        Assert.Equal($"Invalid expression '{text}'.", exception.Message);
    }

    [Fact]
    public void Parse_DynamicWithoutFunction_ThrowsWithExpression()
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(() => Template.Parse("{{$}}"));

        Assert.Equal("Invalid expression '{{$}}'.", exception.Message);
    }

    [Fact]
    public void Parse_TripleBraces_KeepsOuterBracesAsLiterals()
    {
        Template template = Template.Parse("{{{a}}}");

        Assert.Equal(
            [
                new LiteralSegment("{"),
                new VariableSegment("a"),
                new LiteralSegment("}"),
            ],
            template.Segments);
    }

    [Theory]
    [InlineData("x {{a b}} y", 2)]
    [InlineData("{{ok}}\r\n{{c {{a b}}", 12)]
    [InlineData("id={{$fn \"a\"b}}", 3)]
    public void Parse_InvalidExpression_ReportsOffsetOfFailingExpression(string text, int offset)
    {
        HttpFileException exception = Assert.Throws<HttpFileException>(() => Template.Parse(text));

        Assert.Equal(offset, exception.ExpressionOffset);
    }
}
