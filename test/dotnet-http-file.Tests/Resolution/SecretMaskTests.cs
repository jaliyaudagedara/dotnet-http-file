using DotNetHttpFile.Resolution;

namespace DotNetHttpFile.Tests.Resolution;

public sealed class SecretMaskTests
{
    [Fact]
    public void Apply_NoSecrets_ReturnsText()
    {
        SecretMask mask = new();

        Assert.Equal("Bearer abc", mask.Apply("Bearer abc"));
    }

    [Fact]
    public void Apply_SecretInText_ReplacesEveryOccurrence()
    {
        SecretMask mask = new();
        mask.Add("abc");

        Assert.Equal("Bearer ***, again ***.", mask.Apply("Bearer abc, again abc."));
    }

    [Fact]
    public void Apply_AdjacentOccurrences_MasksEach()
    {
        SecretMask mask = new();
        mask.Add("abc");

        Assert.Equal("******", mask.Apply("abcabc"));
    }

    [Fact]
    public void Add_EmptySecret_IsIgnored()
    {
        SecretMask mask = new();
        mask.Add(string.Empty);

        Assert.Equal("text", mask.Apply("text"));
    }

    [Theory]
    [InlineData("abc", "abcdef")]
    [InlineData("abcdef", "abc")]
    public void Apply_SecretContainsAnother_MasksLongest(string first, string second)
    {
        SecretMask mask = new();
        mask.Add(first);
        mask.Add(second);

        Assert.Equal("x *** y *** z", mask.Apply("x abcdef y abc z"));
    }

    [Theory]
    [InlineData("abcd", "cdef")]
    [InlineData("cdef", "abcd")]
    public void Apply_OverlappingSecrets_LeavesNoPartialSecret(string first, string second)
    {
        SecretMask mask = new();
        mask.Add(first);
        mask.Add(second);

        Assert.Equal("x***y", mask.Apply("xabcdefy"));
    }

    [Fact]
    public void Apply_SecretOverlapsItself_MasksWholeRun()
    {
        SecretMask mask = new();
        mask.Add("aba");

        Assert.Equal("x***y", mask.Apply("xababay"));
    }

    [Fact]
    public void Apply_SecretInEscapeDataStringForm_IsMasked()
    {
        SecretMask mask = new();
        mask.Add("p@ss w/rd");

        // Only the Uri.EscapeDataString form is covered, as in a form body. System.Uri writes a URL differently,
        // which is why a resolved URL is printed from ResolvedRequest.DisplayUrl.
        Assert.Equal("key=***&raw=***", mask.Apply("key=p%40ss%20w%2Frd&raw=p@ss w/rd"));
    }

    [Fact]
    public void Apply_SecretInJsonStringForm_IsMasked()
    {
        const string QuotedSecret = """pa"ss\word""";
        SecretMask mask = new();
        mask.Add(QuotedSecret);

        Assert.Equal("""{"token":"***"}""", mask.Apply("""{"token":"pa\"ss\\word"}"""));
    }

    [Fact]
    public void Apply_MultiLineSecretInJsonStringForm_IsMasked()
    {
        SecretMask mask = new();
        mask.Add("line one\nline two\tend");

        Assert.Equal("\"***\"", mask.Apply("""
            "line one\nline two\tend"
            """));
    }

    [Fact]
    public void Apply_SecretInXmlTextForm_IsMasked()
    {
        SecretMask mask = new();
        mask.Add("p&ss<1>\"q\"");

        Assert.Equal("<t>***</t>", mask.Apply("<t>p&amp;ss&lt;1&gt;\"q\"</t>"));
    }

    [Fact]
    public void Apply_SecretInXmlAttributeForm_IsMasked()
    {
        SecretMask mask = new();
        mask.Add("x\"y&z");

        Assert.Equal("<t a=\"***\" />", mask.Apply("<t a=\"x&quot;y&amp;z\" />"));
    }

    [Theory]
    [InlineData(0x07)]
    [InlineData(0xD800)]
    public void Add_SecretThatXmlOrJsonCannotHold_StillMasksTheRawValue(int character)
    {
        string secret = "ab" + (char)character + "cd";
        SecretMask mask = new();
        mask.Add(secret);

        Assert.Equal("[***]", mask.Apply($"[{secret}]"));
    }

    [Fact]
    public void Apply_MatchIsOrdinal_KeepsDifferentCase()
    {
        SecretMask mask = new();
        mask.Add("abc");

        Assert.Equal("ABC", mask.Apply("ABC"));
    }

    // System.Uri sends a space as %20 and leaves '@', '/' and '+' alone, which Uri.EscapeDataString does not.
    [Theory]
    [InlineData("p@ss w/rd+1", "p@ss%20w/rd+1")]
    [InlineData("a b?c d", "a%20b?c%20d")]
    [InlineData("50% off", "50%25%20off")]
    public void Apply_SecretInUriEscaping_Masks(string secret, string onTheWire)
    {
        SecretMask mask = new();
        mask.Add(secret);

        Assert.Equal(
            "GET /echo/*** HTTP/1.1 and /echo?key=***",
            mask.Apply($"GET /echo/{onTheWire} HTTP/1.1 and /echo?key={onTheWire}"));
    }

    // Uri would reduce each of these to a part of the value; masking that part would hide unrelated text.
    [Theory]
    [InlineData("a/../b", "b")]
    [InlineData("xy#z", "xy")]
    [InlineData("#z", "z")]
    public void Apply_SecretThatUriWouldShorten_DoesNotMaskThePart(string secret, string part)
    {
        SecretMask mask = new();
        mask.Add(secret);

        Assert.Equal($"[{part}]", mask.Apply($"[{part}]"));
    }
}
