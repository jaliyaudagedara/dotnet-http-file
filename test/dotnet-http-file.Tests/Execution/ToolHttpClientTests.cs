using System.Net;
using System.Net.Security;
using DotNetHttpFile.Execution;

namespace DotNetHttpFile.Tests.Execution;

public sealed class ToolHttpClientTests
{
    [Fact]
    public void CreateHandler_Insecure_AcceptsAnyCertificate()
    {
        using SocketsHttpHandler handler = ToolHttpClient.CreateHandler(insecure: true);

        RemoteCertificateValidationCallback? callback = handler.SslOptions.RemoteCertificateValidationCallback;

        Assert.NotNull(callback);
        Assert.True(callback(handler, certificate: null, chain: null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void CreateHandler_NotInsecure_LeavesCertificateValidationToThePlatform()
    {
        using SocketsHttpHandler handler = ToolHttpClient.CreateHandler(insecure: false);

        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateHandler_Always_LeavesRedirectsToTheSenderAndAddsNothing(bool insecure)
    {
        using SocketsHttpHandler handler = ToolHttpClient.CreateHandler(insecure);

        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Null(handler.ActivityHeadersPropagator);
        Assert.False(handler.UseCookies);
    }

    [Fact]
    public void Create_Handler_SetsTimeout()
    {
        using HttpClient httpClient = ToolHttpClient.Create(ToolHttpClient.CreateHandler(insecure: false));

        Assert.Equal(TimeSpan.FromSeconds(100), ToolHttpClient.Timeout);
        Assert.Equal(ToolHttpClient.Timeout, httpClient.Timeout);
        Assert.Empty(httpClient.DefaultRequestHeaders);
    }
}
