using System.Text;
using DotNetHttpFile.Secrets;

namespace DotNetHttpFile.Tests.Secrets;

public sealed class AspnetUserSecretsProviderTests : IDisposable
{
    private const string VariableName = "ApiKeyDev";
    private const string UserSecretsId = "0f6a2c5e-6d0b-4a51-9a52-3f1f6f3a8f10";
    private const string ProjectFolder = "project";
    private const string ProjectFileName = "WebApi.csproj";
    private const string RootFolder = "root";
    private const string SecretsFileName = "secrets.json";
    private const string SecretValue = "value-from-user-secrets";
    private const string Definition = """{ "provider": "AspnetUserSecrets", "secretName": "config:ApiKeyDev" }""";

    private const string ProjectFile =
        $"""
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net11.0</TargetFramework>
            <UserSecretsId>{UserSecretsId}</UserSecretsId>
          </PropertyGroup>
        </Project>
        """;

    private const string NestedSecretsFile =
        $$"""
        {
          "config": {
            "ApiKeyDev": "{{SecretValue}}"
          }
        }
        """;

    private readonly TemporaryDirectory _directory = new();

    private string ProjectDirectory => Path.Combine(_directory.DirectoryPath, ProjectFolder);

    private string ProjectFilePath => Path.Combine(ProjectDirectory, ProjectFileName);

    private string RootDirectory => Path.Combine(_directory.DirectoryPath, RootFolder);

    private string SecretsFilePath => Path.Combine(RootDirectory, UserSecretsId, SecretsFileName);

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task ResolveAsync_UserSecretsNestedKey_ReturnsValue()
    {
        await WriteProjectFileAsync(ProjectFile);
        await WriteSecretsFileAsync(NestedSecretsFile);

        string value = await ResolveAsync(Definition);

        Assert.Equal(SecretValue, value);
    }

    [Fact]
    public async Task ResolveAsync_UserSecretsFlatKey_ReturnsValue()
    {
        await WriteProjectFileAsync(ProjectFile);
        await WriteSecretsFileAsync($$"""{ "config:ApiKeyDev": "{{SecretValue}}" }""");

        string value = await ResolveAsync(Definition);

        Assert.Equal(SecretValue, value);
    }

    [Fact]
    public async Task ResolveAsync_UserSecretsKeyInDifferentCase_ReturnsValue()
    {
        await WriteProjectFileAsync(ProjectFile);
        await WriteSecretsFileAsync(NestedSecretsFile);

        string value = await ResolveAsync(
            """{ "provider": "AspnetUserSecrets", "secretName": "CONFIG:apikeydev" }""");

        Assert.Equal(SecretValue, value);
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("1.50", "1.50")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    public async Task ResolveAsync_UserSecretsNumberOrBoolean_ReturnsJsonText(string json, string expected)
    {
        await WriteProjectFileAsync(ProjectFile);
        await WriteSecretsFileAsync($$"""{ "config": { "ApiKeyDev": {{json}} } }""");

        string value = await ResolveAsync(Definition);

        Assert.Equal(expected, value);
    }

    [Fact]
    public async Task ResolveAsync_UserSecretsArrayItem_ReturnsValueByIndex()
    {
        await WriteProjectFileAsync(ProjectFile);
        await WriteSecretsFileAsync($$"""{ "config": { "keys": ["first", "{{SecretValue}}"] } }""");

        string value = await ResolveAsync("""{ "provider": "AspnetUserSecrets", "secretName": "config:keys:1" }""");

        Assert.Equal(SecretValue, value);
    }

    [Fact]
    public async Task ResolveAsync_UserSecretsFileWithBomAndComments_ReturnsValue()
    {
        await WriteProjectFileAsync(ProjectFile);
        string content = "// user secrets" + Environment.NewLine + NestedSecretsFile;
        Directory.CreateDirectory(Path.Combine(RootDirectory, UserSecretsId));
        await File.WriteAllBytesAsync(
            SecretsFilePath,
            [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(content)],
            TestContext.Current.CancellationToken);

        string value = await ResolveAsync(Definition);

        Assert.Equal(SecretValue, value);
    }

    [Fact]
    public async Task ResolveAsync_UserSecretsProjectWithXmlNamespace_ReturnsValue()
    {
        await WriteProjectFileAsync(
            $"""
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <UserSecretsId> {UserSecretsId} </UserSecretsId>
              </PropertyGroup>
            </Project>
            """);
        await WriteSecretsFileAsync(NestedSecretsFile);

        string value = await ResolveAsync(Definition);

        Assert.Equal(SecretValue, value);
    }

    [Fact]
    public async Task ResolveAsync_UserSecretsNoProject_Throws()
    {
        _directory.CreateDirectory(ProjectFolder);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => ResolveAsync(Definition));

        Assert.Equal(
            $"Variable 'ApiKeyDev' (AspnetUserSecrets): no project file was found in {ProjectDirectory}.",
            exception.Message);
    }

    [Theory]
    [InlineData("<Project><PropertyGroup /></Project>")]
    [InlineData("<Project><PropertyGroup><UserSecretsId> </UserSecretsId></PropertyGroup></Project>")]
    public async Task ResolveAsync_UserSecretsNoUserSecretsId_Throws(string projectFile)
    {
        await WriteProjectFileAsync(projectFile);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => ResolveAsync(Definition));

        Assert.Equal(
            $"Variable 'ApiKeyDev' (AspnetUserSecrets): project file {ProjectFilePath} has no UserSecretsId.",
            exception.Message);
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("a/b")]
    [InlineData("..")]
    public async Task ResolveAsync_UserSecretsIdWithPathSeparator_Throws(string userSecretsId)
    {
        await WriteProjectFileAsync(
            $"<Project><PropertyGroup><UserSecretsId>{userSecretsId}</UserSecretsId></PropertyGroup></Project>");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => ResolveAsync(Definition));

        Assert.Equal(
            $"Variable 'ApiKeyDev' (AspnetUserSecrets): project file {ProjectFilePath} has an invalid "
                + "UserSecretsId.",
            exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_UserSecretsProjectNotXml_Throws()
    {
        await WriteProjectFileAsync("<Project><PropertyGroup>");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => ResolveAsync(Definition));

        Assert.Equal(
            $"Variable 'ApiKeyDev' (AspnetUserSecrets): project file {ProjectFilePath} is not valid XML.",
            exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_UserSecretsFileIsDirectory_ThrowsFileReadFailure()
    {
        await WriteProjectFileAsync(ProjectFile);
        _directory.CreateDirectory(Path.Combine(RootFolder, UserSecretsId, SecretsFileName));

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => ResolveAsync(Definition));

        Assert.StartsWith("Could not read a file: ", exception.Message, StringComparison.Ordinal);
        Assert.Contains(SecretsFilePath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_UserSecretsFileMissing_Throws()
    {
        await WriteProjectFileAsync(ProjectFile);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => ResolveAsync(Definition));

        Assert.Equal(
            $"Variable 'ApiKeyDev' (AspnetUserSecrets): user secrets file {SecretsFilePath} was not found.",
            exception.Message);
    }

    [Theory]
    [InlineData("""{ "other": "value-from-user-secrets" }""")]
    [InlineData("""{ "config": { "ApiKeyDev": null } }""")]
    [InlineData("""{ "config": { "ApiKeyDev": { "nested": "value-from-user-secrets" } } }""")]
    public async Task ResolveAsync_UserSecretsKeyNotFound_ThrowsWithoutValues(string secretsFile)
    {
        await WriteProjectFileAsync(ProjectFile);
        await WriteSecretsFileAsync(secretsFile);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => ResolveAsync(Definition));

        Assert.Equal(
            $"Variable 'ApiKeyDev' (AspnetUserSecrets): secret 'config:ApiKeyDev' was not found in "
                + $"{SecretsFilePath}.",
            exception.Message);
    }

    [Theory]
    [InlineData("""{ "config": { "ApiKeyDev": "value-from-user-secrets" """)]
    [InlineData("""["value-from-user-secrets"]""")]
    [InlineData("""{ "a": "value-from-user-secrets", "a": "value-from-user-secrets" }""")]
    [InlineData("""{ "config:ApiKeyDev": "value-from-user-secrets", "config": { "ApiKeyDev": "other" } }""")]
    [InlineData("""{ "config": { "ApiKeyDev": "value-from-user-secrets", "apikeydev": "other" } }""")]
    [InlineData("""{ "config": { "ApiKeyDev": "value-from-user-secrets", "Other": "\ud800" } }""")]
    [InlineData("""{ "config": { "ApiKeyDev": "value-from-user-secrets", "\udc00": "x" } }""")]
    public async Task ResolveAsync_UserSecretsFileInvalid_ThrowsWithoutValues(string secretsFile)
    {
        await WriteProjectFileAsync(ProjectFile);
        await WriteSecretsFileAsync(secretsFile);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => ResolveAsync(Definition));

        Assert.Equal(
            $"Variable 'ApiKeyDev' (AspnetUserSecrets): user secrets file {SecretsFilePath} is not a valid JSON "
                + "object.",
            exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_UserSecretsFileNotUtf8_ThrowsWithoutValues()
    {
        const byte Windows1252EAcute = 0xE9;
        await WriteProjectFileAsync(ProjectFile);
        Directory.CreateDirectory(Path.Combine(RootDirectory, UserSecretsId));
        await File.WriteAllBytesAsync(
            SecretsFilePath,
            [
                .. Encoding.ASCII.GetBytes("""{ "config": { "ApiKeyDev": "caf"""),
                Windows1252EAcute,
                .. Encoding.ASCII.GetBytes("\" } }"),
            ],
            TestContext.Current.CancellationToken);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => ResolveAsync(Definition));

        Assert.Equal(
            $"Variable 'ApiKeyDev' (AspnetUserSecrets): user secrets file {SecretsFilePath} is not a valid JSON "
                + "object.",
            exception.Message);
    }

    [Theory]
    [InlineData("""{ "provider": "AspnetUserSecrets" }""")]
    [InlineData("""{ "provider": "AspnetUserSecrets", "secretName": "" }""")]
    [InlineData("""{ "provider": "AspnetUserSecrets", "secretName": 1 }""")]
    public async Task ResolveAsync_UserSecretsMissingSecretName_Throws(string definitionJson)
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveAsync(definitionJson));

        Assert.Equal("Variable 'ApiKeyDev' (AspnetUserSecrets) is missing 'secretName'.", exception.Message);
    }

    [Fact]
    public void DefaultRoot_CurrentPlatform_ReturnsUserSecretsFolder()
    {
        string expected = OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "UserSecrets")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".microsoft",
                "usersecrets");

        string root = AspnetUserSecretsProvider.DefaultRoot();

        Assert.Equal(expected, root);
    }

    private Task<string> WriteProjectFileAsync(string content) =>
        _directory.WriteFileAsync(
            Path.Combine(ProjectFolder, ProjectFileName),
            content,
            TestContext.Current.CancellationToken);

    private Task<string> WriteSecretsFileAsync(string content) =>
        _directory.WriteFileAsync(
            Path.Combine(RootFolder, UserSecretsId, SecretsFileName),
            content,
            TestContext.Current.CancellationToken);

    private Task<string> ResolveAsync(string definitionJson)
    {
        AspnetUserSecretsProvider provider = new(RootDirectory);

        return provider.ResolveAsync(
            SecretRequestFactory.Create(VariableName, definitionJson, ProjectDirectory),
            TestContext.Current.CancellationToken);
    }
}
