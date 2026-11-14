using Azure.Core;

namespace DotNetHttpFile.Tests.Secrets;

internal sealed class FakeTokenCredential(string token = FakeTokenCredential.DefaultToken, Exception? failure = null)
    : TokenCredential
{
    public const string DefaultToken = "token-1";

    public TokenRequestContext? RequestContext { get; private set; }

    public CancellationToken CancellationToken { get; private set; }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        RequestContext = requestContext;
        CancellationToken = cancellationToken;

        if (failure is not null)
        {
            throw failure;
        }

        return new AccessToken(token, DateTimeOffset.MaxValue);
    }

    public override ValueTask<AccessToken> GetTokenAsync(
        TokenRequestContext requestContext,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
