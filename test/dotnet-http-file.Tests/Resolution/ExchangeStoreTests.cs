using DotNetHttpFile.Execution;
using DotNetHttpFile.Resolution;

namespace DotNetHttpFile.Tests.Resolution;

public sealed class ExchangeStoreTests
{
    [Fact]
    public void TryGet_AddedName_ReturnsExchange()
    {
        ExchangeStore store = new();
        HttpExchange exchange = CreateExchange();
        store.Add("login", exchange);

        bool found = store.TryGet("login", out HttpExchange? stored);

        Assert.True(found);
        Assert.Same(exchange, stored);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("Login")]
    public void TryGet_UnknownOrDifferentlyCasedName_ReturnsFalse(string requestName)
    {
        ExchangeStore store = new();
        store.Add("login", CreateExchange());

        bool found = store.TryGet(requestName, out HttpExchange? stored);

        Assert.False(found);
        Assert.Null(stored);
    }

    [Fact]
    public void Add_DuplicateName_ThrowsInvalidOperationException()
    {
        ExchangeStore store = new();
        store.Add("login", CreateExchange());

        Assert.Throws<InvalidOperationException>(() => store.Add("login", CreateExchange()));
    }

    private static HttpExchange CreateExchange() =>
        new(
            new ResolvedRequest("GET", new Uri("http://localhost:5199/json"), [], "http://localhost:5199/json"),
            new ReceivedResponse(new Version(1, 1), 200, "OK", [], ReadOnlyMemory<byte>.Empty, TimeSpan.Zero));
}
