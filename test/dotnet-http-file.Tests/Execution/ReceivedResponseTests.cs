using DotNetHttpFile.Execution;

namespace DotNetHttpFile.Tests.Execution;

public sealed class ReceivedResponseTests
{
    [Fact]
    public void ContentType_SeveralHeadersInAnyCase_ReturnsFirstValue()
    {
        ReceivedResponse response = CreateResponse(
            200,
            [
                new("Date", "Sat, 14 Nov 2026 00:00:00 GMT"),
                new("content-type", "application/json; charset=utf-8"),
                new("Content-Type", "text/plain"),
            ]);

        Assert.Equal("application/json; charset=utf-8", response.ContentType);
    }

    [Fact]
    public void ContentType_NoHeader_ReturnsNull()
    {
        ReceivedResponse response = CreateResponse(200, []);

        Assert.Null(response.ContentType);
    }

    [Theory]
    [InlineData("gzip", true)]
    [InlineData("GZIP", true)]
    [InlineData("identity, br", true)]
    [InlineData("identity", false)]
    [InlineData("Identity", false)]
    [InlineData("", false)]
    public void IsContentEncoded_ContentEncodingHeader_IsTrueUnlessIdentity(string contentEncoding, bool expected)
    {
        ReceivedResponse response = CreateResponse(200, [new("content-encoding", contentEncoding)]);

        Assert.Equal(expected, response.IsContentEncoded);
    }

    [Fact]
    public void IsContentEncoded_NoHeader_IsFalse()
    {
        ReceivedResponse response = CreateResponse(200, [new("Content-Type", "application/json")]);

        Assert.False(response.IsContentEncoded);
    }

    [Theory]
    [InlineData(199, false)]
    [InlineData(200, true)]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(404, false)]
    public void IsSuccess_StatusCode_IsTrueOnlyFor2xx(int statusCode, bool expected)
    {
        ReceivedResponse response = CreateResponse(statusCode, []);

        Assert.Equal(expected, response.IsSuccess);
    }

    private static ReceivedResponse CreateResponse(
        int statusCode,
        IReadOnlyList<KeyValuePair<string, string>> headers) =>
        new(new Version(1, 1), statusCode, "Reason", headers, ReadOnlyMemory<byte>.Empty, TimeSpan.Zero);
}
