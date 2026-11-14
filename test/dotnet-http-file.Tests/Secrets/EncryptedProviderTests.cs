using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DotNetHttpFile.Secrets;

namespace DotNetHttpFile.Tests.Secrets;

public sealed class EncryptedProviderTests
{
    private const string VariableName = "dpapiValue";
    private const string PlainText = "Hello, World!";
    private const string WindowsOnly = "DPAPI only exists on Windows.";
    private const string NotBase64 = "not base64!";

    [Fact]
    public async Task ResolveAsync_EncryptedRoundTrip_ReturnsPlainText()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        string encrypted = Encrypt(PlainText);

        string value = await ResolveAsync(Definition(encrypted));

        Assert.Equal(PlainText, value);
    }

    [Fact]
    public async Task ResolveAsync_EncryptedOnNonWindows_Throws()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The Encrypted provider works on Windows.");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(Definition(NotBase64)));

        Assert.Equal(
            "Variable 'dpapiValue' uses the Encrypted (DPAPI) provider, which only works on Windows.",
            exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_EncryptedNotBase64_ThrowsWithoutValue()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(Definition(NotBase64)));

        Assert.Equal("Variable 'dpapiValue' (Encrypted): 'value' is not valid base64.", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_EncryptedNotDpapiData_ThrowsWithoutValue()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        string notEncrypted = Convert.ToBase64String(Encoding.Unicode.GetBytes(PlainText));

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(Definition(notEncrypted)));

        Assert.Equal(
            "Variable 'dpapiValue' (Encrypted): 'value' could not be decrypted. A DPAPI value only works for the "
                + "Windows user and machine that encrypted it.",
            exception.Message);
    }

    [Theory]
    [InlineData("""{ "provider": "Encrypted" }""")]
    [InlineData("""{ "provider": "Encrypted", "value": "" }""")]
    [InlineData("""{ "provider": "Encrypted", "value": 12 }""")]
    public async Task ResolveAsync_EncryptedMissingValue_Throws(string definitionJson)
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(definitionJson));

        Assert.Equal("Variable 'dpapiValue' (Encrypted) is missing 'value'.", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_EncryptedMissingValue_FailsThroughTheTask()
    {
        EncryptedProvider provider = new();

        Task<string> task = provider.ResolveAsync(
            SecretRequestFactory.Create(VariableName, """{ "provider": "Encrypted" }"""),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HttpFileException>(() => task);
    }

    private static string Definition(string value)
    {
        JsonObject definition = new()
        {
            ["provider"] = EncryptedProvider.ProviderName,
            ["value"] = value,
        };

        return definition.ToJsonString();
    }

    // Follows the console application in the Visual Studio .http file documentation.
    private static string Encrypt(string stringToEncrypt)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(WindowsOnly);
        }

        byte[] encryptedBytes = ProtectedData.Protect(
            Encoding.Unicode.GetBytes(stringToEncrypt),
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);

        return Convert.ToBase64String(encryptedBytes);
    }

    private static Task<string> ResolveAsync(string definitionJson)
    {
        EncryptedProvider provider = new();

        return provider.ResolveAsync(
            SecretRequestFactory.Create(VariableName, definitionJson),
            TestContext.Current.CancellationToken);
    }
}
