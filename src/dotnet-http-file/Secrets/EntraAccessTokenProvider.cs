using System.Buffers.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using DotNetHttpFile.Environments;
using DotNetHttpFile.Output;

namespace DotNetHttpFile.Secrets;

/// <summary>
/// Resolves a variable to a Microsoft Entra access token from the shared credential.
/// </summary>
/// <param name="credential">The credential the Azure providers share.</param>
/// <param name="log">
/// Receives the scope, tenant and expiry of each token and the credential that supplied it, never the token.
/// </param>
internal sealed partial class EntraAccessTokenProvider(Lazy<TokenCredential> credential, DiagnosticLog? log = null)
    : ISecretProvider
{
    public const string ProviderName = "EntraAccessToken";

    private const string ScopeField = "scope";
    private const string TenantIdField = "tenantId";
    private const string LoginCommand = "az login";
    private const string LoginToTenantCommand = "az login --tenant ";
    private const string TenantIdClaim = "tid";
    private const char JwtSeparator = '.';
    private const int JwtPartCount = 3;
    private const string ExpiryFormat = "u";

    public string Name => ProviderName;

    public async Task<string> ResolveAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string scope = request.GetRequiredString(ProviderName, ScopeField);
        if (!ScopePattern.IsMatch(scope))
        {
            throw new HttpFileException(Messages.Format(Resources.EntraAccessTokenInvalidScope, request.VariableName));
        }

        string? tenantId = request.GetOptionalString(ProviderName, TenantIdField);
        if (tenantId is not null && !TenantIdPattern.IsMatch(tenantId))
        {
            throw new HttpFileException(Messages.Format(
                Resources.EntraAccessTokenInvalidTenantId,
                request.VariableName));
        }

        TokenRequestContext requestContext = new([scope], tenantId: tenantId);

        try
        {
            AccessToken accessToken;
            using (AzureCredentialLog.Listen(log))
            {
                accessToken = await credential.Value
                    .GetTokenAsync(requestContext, cancellationToken)
                    .ConfigureAwait(false);
            }

            LogToken(scope, accessToken);

            return accessToken.Token;
        }
        catch (AuthenticationFailedException exception)
        {
            // CredentialUnavailableException derives from AuthenticationFailedException, so this covers both.
            throw new HttpFileException(
                Messages.Format(
                    Resources.EntraAccessTokenFailed,
                    request.VariableName,
                    scope,
                    tenantId ?? Resources.EntraAccessTokenDefaultTenant,
                    exception.Message,
                    tenantId is null ? LoginCommand : LoginToTenantCommand + tenantId,
                    Environment.NewLine),
                exception);
        }
    }

    // The tid claim of a JWT access token. The signature is not checked: the value is only shown, never trusted.
    private static string? TenantOf(string token)
    {
        string[] parts = token.Split(JwtSeparator);
        if (parts.Length != JwtPartCount)
        {
            return null;
        }

        try
        {
            return JsonFile.TryParse(Base64Url.DecodeFromChars(parts[1]), out JsonNode? payload)
                && payload is JsonObject claims
                && claims[TenantIdClaim] is JsonValue tenant
                && tenant.GetValueKind() == JsonValueKind.String
                    ? tenant.GetValue<string>()
                    : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private void LogToken(string scope, AccessToken accessToken)
    {
        if (log is not { IsEnabled: true })
        {
            return;
        }

        log.Write(Messages.Format(
            Resources.VerboseToken,
            scope,
            TenantOf(accessToken.Token) ?? Resources.VerboseTokenTenantUnknown,
            accessToken.ExpiresOn.ToUniversalTime().ToString(ExpiryFormat, CultureInfo.InvariantCulture)));
    }

    // Azure.Core's scope check for the developer-tool credentials allows only these characters, and the scope
    // is printed in the failure message, so it is checked first.
    [GeneratedRegex(@"^[0-9A-Za-z_.:/-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex ScopePattern { get; }

    // A tenant ID or a domain name. The value is printed in the suggested "az login --tenant" command, so
    // nothing that a shell or a terminal would act on may pass.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex TenantIdPattern { get; }
}
