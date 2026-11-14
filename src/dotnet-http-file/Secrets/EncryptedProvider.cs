using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace DotNetHttpFile.Secrets;

internal sealed class EncryptedProvider : ISecretProvider
{
    public const string ProviderName = "Encrypted";

    private const string ValueField = "value";

    private static readonly CompositeFormat s_encryptedCannotDecryptFormat =
        CompositeFormat.Parse(Resources.EncryptedCannotDecrypt);

    private static readonly CompositeFormat s_encryptedInvalidBase64Format =
        CompositeFormat.Parse(Resources.EncryptedInvalidBase64);

    private static readonly CompositeFormat s_encryptedWindowsOnlyFormat =
        CompositeFormat.Parse(Resources.EncryptedWindowsOnly);

    public string Name => ProviderName;

    public Task<string> ResolveAsync(SecretRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // There is nothing to await, so a failure is handed back through the task as an async method would.
        try
        {
            return Task.FromResult(Resolve(request));
        }
        catch (HttpFileException exception)
        {
            return Task.FromException<string>(exception);
        }
    }

    private static string Resolve(SecretRequest request)
    {
        string value = request.GetRequiredString(ProviderName, ValueField);
        if (!OperatingSystem.IsWindows())
        {
            throw new HttpFileException(
                string.Format(CultureInfo.CurrentCulture, s_encryptedWindowsOnlyFormat, request.VariableName));
        }

        return Unprotect(request.VariableName, value);
    }

    // Mirrors the encryption sample in the Visual Studio .http file documentation: UTF-16 text, no entropy,
    // current-user scope, base64. The exceptions stay as inner exceptions only, so the value is never echoed.
    [SupportedOSPlatform("windows")]
    private static string Unprotect(string variableName, string value)
    {
        try
        {
            byte[] decrypted = ProtectedData.Unprotect(
                Convert.FromBase64String(value),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            return Encoding.Unicode.GetString(decrypted);
        }
        catch (FormatException exception)
        {
            throw new HttpFileException(
                string.Format(CultureInfo.CurrentCulture, s_encryptedInvalidBase64Format, variableName),
                exception);
        }
        catch (CryptographicException exception)
        {
            throw new HttpFileException(
                string.Format(CultureInfo.CurrentCulture, s_encryptedCannotDecryptFormat, variableName),
                exception);
        }
    }
}
