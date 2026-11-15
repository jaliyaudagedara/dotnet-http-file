using DotNetHttpFile.Execution;
using DotNetHttpFile.Tests.Support;

namespace DotNetHttpFile.Tests.Execution;

public sealed class ContentDecoderTests
{
    private static readonly byte[] s_content = """{"title": "Write blog post"}"""u8.ToArray();

    [Theory]
    [InlineData("gzip", true)]
    [InlineData("GZIP", true)]
    [InlineData("x-gzip", true)]
    [InlineData("deflate", true)]
    [InlineData("Deflate", true)]
    [InlineData("br", false)]
    [InlineData("compress", false)]
    [InlineData("zstd", false)]
    public void IsSupported_Coding_IsTrueOnlyForGzipAndDeflate(string coding, bool expected)
    {
        Assert.Equal(expected, ContentDecoder.IsSupported(coding));
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("x-gzip")]
    public void Decode_Gzip_ReturnsContent(string coding)
    {
        byte[]? decoded = ContentDecoder.Decode(
            Compression.Gzip(s_content),
            coding,
            ContentDecoder.MaxDecodedLength,
            TestContext.Current.CancellationToken);

        Assert.Equal(s_content, decoded);
    }

    [Fact]
    public void Decode_ZlibWrappedDeflate_ReturnsContent()
    {
        byte[]? decoded = ContentDecoder.Decode(
            Compression.Zlib(s_content),
            "deflate",
            ContentDecoder.MaxDecodedLength,
            TestContext.Current.CancellationToken);

        Assert.Equal(s_content, decoded);
    }

    [Fact]
    public void Decode_RawDeflate_ReturnsContent()
    {
        byte[]? decoded = ContentDecoder.Decode(
            Compression.RawDeflate(s_content),
            "deflate",
            ContentDecoder.MaxDecodedLength,
            TestContext.Current.CancellationToken);

        Assert.Equal(s_content, decoded);
    }

    [Fact]
    public void Decode_GzipHeaderFollowedByGarbage_ThrowsInvalidDataException()
    {
        byte[] body = [0x1F, 0x8B, 0x08, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

        Assert.Throws<InvalidDataException>(
            () => ContentDecoder.Decode(
                body,
                "gzip",
                ContentDecoder.MaxDecodedLength,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Decode_ZlibHeaderFollowedByGarbage_ThrowsInvalidDataException()
    {
        byte[] body = [0x78, 0x9C, 0xFF, 0xFF, 0xFF, 0xFF];

        Assert.Throws<InvalidDataException>(
            () => ContentDecoder.Decode(
                body,
                "deflate",
                ContentDecoder.MaxDecodedLength,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Decode_ContentLongerThanTheLimit_ReturnsNull()
    {
        byte[]? decoded = ContentDecoder.Decode(
            Compression.Gzip(s_content),
            "gzip",
            s_content.Length - 1,
            TestContext.Current.CancellationToken);

        Assert.Null(decoded);
    }

    [Fact]
    public void Decode_ContentExactlyAtTheLimit_ReturnsContent()
    {
        byte[]? decoded = ContentDecoder.Decode(
            Compression.Gzip(s_content),
            "gzip",
            s_content.Length,
            TestContext.Current.CancellationToken);

        Assert.Equal(s_content, decoded);
    }

    [Fact]
    public void Decode_Cancelled_ThrowsOperationCanceled()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => ContentDecoder.Decode(
                Compression.Gzip(s_content),
                "gzip",
                ContentDecoder.MaxDecodedLength,
                cancellation.Token));
    }
}
