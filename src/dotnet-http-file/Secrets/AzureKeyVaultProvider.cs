using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace DotNetHttpFile.Secrets;

internal sealed partial class AzureKeyVaultProvider(
    Lazy<TokenCredential> credential,
    Func<Uri, TokenCredential, SecretClient> createClient) : ISecretProvider
{
    public const string ProviderName = "AzureKeyVault";

    private const string ResourceIdField = "resourceId";
    private const string SecretNameField = "secretName";
    private const char ResourceIdSeparator = '/';
    private const int NoResponseStatus = 0;

    private static readonly CompositeFormat s_keyVaultForbiddenFormat =
        CompositeFormat.Parse(Resources.KeyVaultForbidden);

    private static readonly CompositeFormat s_keyVaultInvalidResourceIdFormat =
        CompositeFormat.Parse(Resources.KeyVaultInvalidResourceId);

    private static readonly CompositeFormat s_keyVaultRequestFailedFormat =
        CompositeFormat.Parse(Resources.KeyVaultRequestFailed);

    private static readonly CompositeFormat s_keyVaultSecretNotFoundFormat =
        CompositeFormat.Parse(Resources.KeyVaultSecretNotFound);

    private static readonly CompositeFormat s_keyVaultSignInFailedFormat =
        CompositeFormat.Parse(Resources.KeyVaultSignInFailed);

    private static readonly CompositeFormat s_keyVaultUnreachableFormat =
        CompositeFormat.Parse(Resources.KeyVaultUnreachable);

    public string Name => ProviderName;

    public async Task<string> ResolveAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string resourceId = request.GetRequiredString(ProviderName, ResourceIdField);
        string secretName = request.GetRequiredString(ProviderName, SecretNameField);
        string vaultName = GetVaultName(request.VariableName, resourceId);
        Uri vaultUri = new($"https://{vaultName}.vault.azure.net/");

        try
        {
            SecretClient client = createClient(vaultUri, credential.Value);
            Response<KeyVaultSecret> response = await client
                .GetSecretAsync(secretName, version: null, cancellationToken)
                .ConfigureAwait(false);

            return response.Value.Value;
        }
        catch (RequestFailedException exception)
        {
            throw new HttpFileException(
                RequestFailedMessage(exception.Status, request.VariableName, vaultName, secretName),
                exception);
        }
        catch (AggregateException exception) when (cancellationToken.IsCancellationRequested)
        {
            // A cancellation that lands during the retries arrives wrapped with the earlier failures, and the
            // caller has to see it as a cancellation.
            throw new OperationCanceledException(message: null, exception, cancellationToken);
        }
        catch (AggregateException exception) when (IsRetriedConnectionFailure(exception))
        {
            // Azure.Core's retry policy throws the first failure as is only when it was the single attempt.
            // After retries it throws an AggregateException holding the failure of every attempt.
            int status = exception.InnerExceptions.OfType<RequestFailedException>().LastOrDefault()?.Status
                ?? NoResponseStatus;

            throw new HttpFileException(
                RequestFailedMessage(status, request.VariableName, vaultName, secretName),
                exception);
        }
        catch (AuthenticationFailedException exception)
        {
            // CredentialUnavailableException derives from AuthenticationFailedException, so this covers both.
            throw new HttpFileException(
                Messages.Format(
                    s_keyVaultSignInFailedFormat,
                    request.VariableName,
                    vaultName,
                    secretName,
                    exception.Message,
                    Environment.NewLine),
                exception);
        }
    }

    // The name becomes part of the host that receives the access token, so anything that is not a vault
    // name is rejected instead of being sent to whatever host it would spell.
    [GeneratedRegex(@"^(?=.{3,24}\z)[A-Za-z](?:-?[A-Za-z0-9])+\z", RegexOptions.CultureInvariant)]
    private static partial Regex VaultNamePattern { get; }

    // The failures the retry policy retries: a failed request, a broken connection, and a per-try timeout,
    // which surfaces as a cancellation the caller did not ask for.
    private static bool IsRetriedConnectionFailure(AggregateException exception) =>
        exception.InnerExceptions.Count > 0
        && exception.InnerExceptions.All(innerException =>
            innerException is RequestFailedException or IOException or OperationCanceledException);

    private static string GetVaultName(string variableName, string resourceId)
    {
        string vaultName = resourceId[(resourceId.LastIndexOf(ResourceIdSeparator) + 1)..];
        if (!VaultNamePattern.IsMatch(vaultName))
        {
            throw new HttpFileException(Messages.Format(s_keyVaultInvalidResourceIdFormat, variableName));
        }

        return vaultName;
    }

    // The service's own message can echo request details, so only the status reaches the user.
    private static string RequestFailedMessage(int status, string variableName, string vaultName, string secretName)
    {
        CompositeFormat format = status switch
        {
            (int)HttpStatusCode.NotFound => s_keyVaultSecretNotFoundFormat,
            (int)HttpStatusCode.Forbidden => s_keyVaultForbiddenFormat,
            NoResponseStatus => s_keyVaultUnreachableFormat,
            _ => s_keyVaultRequestFailedFormat,
        };

        return Messages.Format(format, variableName, vaultName, secretName, status);
    }
}
