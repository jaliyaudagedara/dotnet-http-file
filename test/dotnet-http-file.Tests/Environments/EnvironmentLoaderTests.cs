using System.Text;
using System.Text.Json;
using DotNetHttpFile.Environments;
using DotNetHttpFile.Tests.Support;

namespace DotNetHttpFile.Tests.Environments;

public sealed class EnvironmentLoaderTests : IDisposable
{
    private const string HttpFileName = "api.http";
    private const string UserFileName = EnvironmentFileLocator.FileName + ".user";

    private const string SharedEnvironmentFile =
        """
        {
            "$shared": {
                "HostAddress": "https://localhost:7293"
            },
            "dev1": {
                "username": "dev1user"
            },
            "dev2": {
                "username": "dev2user"
            },
            "staging": {
                "username": "staginguser",
                "HostAddress": "https://staging.contoso.com"
            }
        }
        """;

    private const string UserScenarioEnvironmentFile =
        """
        {
          "dev": {
            "HostAddress": "https://localhost:7128",
            "Path": "/weatherforecast"
          },
          "remote": {
            "HostAddress": "https://contoso.com",
            "Path": "/weatherforecast"
          }
        }
        """;

    private const string UserScenarioUserFile =
        """
        {
          "dev": {
            "Path": "/swagger/index.html"
          }
        }
        """;

    private readonly TemporaryDirectory _directory = new();

    private string HttpFilePath => Path.Combine(_directory.DirectoryPath, HttpFileName);

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task LoadAsync_SharedAndNamed_NamedOverridesShared()
    {
        string path = await WriteEnvironmentFileAsync(SharedEnvironmentFile);

        EnvironmentSet dev1 = await LoadAsync(path, "dev1");
        EnvironmentSet staging = await LoadAsync(path, "staging");

        Assert.Equal(
            ("https://localhost:7293", "dev1user", "https://staging.contoso.com", "staginguser"),
            (Literal(dev1, "HostAddress"), Literal(dev1, "username"), Literal(staging, "HostAddress"),
                Literal(staging, "username")));
    }

    [Fact]
    public async Task LoadAsync_UserFile_OverridesEnvironment()
    {
        string path = await WriteEnvironmentFileAsync(UserScenarioEnvironmentFile);
        await WriteUserFileAsync(UserScenarioUserFile);

        EnvironmentSet environment = await LoadAsync(path, "dev");

        Assert.Equal(
            ("https://localhost:7128", "/swagger/index.html"),
            (Literal(environment, "HostAddress"), Literal(environment, "Path")));
    }

    [Fact]
    public async Task LoadAsync_UserFileForOtherEnvironment_LeavesEnvironmentValue()
    {
        string path = await WriteEnvironmentFileAsync(UserScenarioEnvironmentFile);
        await WriteUserFileAsync(UserScenarioUserFile);

        EnvironmentSet environment = await LoadAsync(path, "remote");

        Assert.Equal("/weatherforecast", Literal(environment, "Path"));
    }

    [Fact]
    public async Task LoadAsync_ProviderObject_ReturnsProviderValue()
    {
        string path = await WriteEnvironmentFileAsync(
            """
            {
              "dev": {
                "Token": {
                  "provider": "EntraAccessToken",
                  "scope": "api://example/.default",
                  "tenantId": "00000000-0000-0000-0000-000000000001"
                }
              }
            }
            """);

        EnvironmentSet environment = await LoadAsync(path, "dev");

        Assert.True(environment.TryGetValue("Token", out EnvironmentValue? value));
        ProviderEnvironmentValue provider = Assert.IsType<ProviderEnvironmentValue>(value);
        Assert.Equal(
            ("EntraAccessToken", "api://example/.default", "00000000-0000-0000-0000-000000000001"),
            (provider.Provider, (string?)provider.Definition["scope"], (string?)provider.Definition["tenantId"]));
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("-1.50", "-1.50")]
    [InlineData("1e3", "1e3")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    public async Task LoadAsync_NumberValue_ReturnsJsonText(string json, string expected)
    {
        string path = await WriteEnvironmentFileAsync($$"""{ "dev": { "Value": {{json}} } }""");

        EnvironmentSet environment = await LoadAsync(path, "dev");

        Assert.Equal(expected, Literal(environment, "Value"));
    }

    [Fact]
    public async Task LoadAsync_StringWithExpression_ReturnsItLiterally()
    {
        string path = await WriteEnvironmentFileAsync(
            """{ "dev": { "Url": "https://{{Host}}/a\tb", "Host": "localhost" } }""");

        EnvironmentSet environment = await LoadAsync(path, "dev");

        Assert.Equal("https://{{Host}}/a\tb", Literal(environment, "Url"));
    }

    [Fact]
    public async Task LoadAsync_UnknownEnvironment_ThrowsWithAvailableNames()
    {
        string path = await WriteEnvironmentFileAsync(SharedEnvironmentFile);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => LoadAsync(path, "prod"));

        Assert.Equal($"Environment 'prod' not found in {path}. Available: dev1, dev2, staging.", exception.Message);
    }

    [Theory]
    [InlineData("""{ "$shared": { "A": "1" } }""")]
    [InlineData("{}")]
    public async Task LoadAsync_UnknownEnvironmentAndNoneDefined_ThrowsWithoutAvailableNames(string content)
    {
        string path = await WriteEnvironmentFileAsync(content);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => LoadAsync(path, "dev"));

        Assert.Equal($"Environment 'dev' not found in {path}, which defines no environments.", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_SharedGivenAsEnvironmentName_ThrowsNotFound()
    {
        string path = await WriteEnvironmentFileAsync(SharedEnvironmentFile);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => LoadAsync(path, "$shared"));

        Assert.Equal(
            $"Environment '$shared' not found in {path}. Available: dev1, dev2, staging.",
            exception.Message);
    }

    [Fact]
    public async Task LoadAsync_EnvironmentNameDifferingByCase_ThrowsNotFound()
    {
        string path = await WriteEnvironmentFileAsync(UserScenarioEnvironmentFile);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => LoadAsync(path, "Dev"));

        Assert.Equal($"Environment 'Dev' not found in {path}. Available: dev, remote.", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_CommentsAndTrailingCommas_Accepted()
    {
        string path = await WriteEnvironmentFileAsync(
            """
            // line comment
            {
              /* block comment */
              "dev": {
                "HostAddress": "https://localhost:44320", // trailing comment
              },
            }
            """);

        EnvironmentSet environment = await LoadAsync(path, "dev");

        Assert.Equal("https://localhost:44320", Literal(environment, "HostAddress"));
    }

    [Fact]
    public async Task LoadAsync_NoEnvironmentName_AppliesSharedOnly()
    {
        string path = await WriteEnvironmentFileAsync(SharedEnvironmentFile);

        EnvironmentSet environment = await LoadAsync(path, environmentName: null);

        Assert.Equal(
            ("https://localhost:7293", false, null),
            (Literal(environment, "HostAddress"), environment.TryGetValue("username", out _),
                environment.SelectedName));
    }

    [Fact]
    public async Task LoadAsync_NoEnvironmentName_UserSharedOverridesShared()
    {
        string path = await WriteEnvironmentFileAsync(SharedEnvironmentFile);
        await WriteUserFileAsync("""{ "$shared": { "HostAddress": "https://localhost:1" } }""");

        EnvironmentSet environment = await LoadAsync(path, environmentName: null);

        Assert.Equal("https://localhost:1", Literal(environment, "HostAddress"));
    }

    [Fact]
    public async Task LoadAsync_AllFourLayers_AppliesPrecedence()
    {
        string path = await WriteEnvironmentFileAsync(
            """
            {
              "$shared": { "A": "shared", "B": "shared", "C": "shared", "D": "shared" },
              "dev": { "C": "named", "D": "named" }
            }
            """);
        await WriteUserFileAsync(
            """
            {
              "$shared": { "B": "user-shared", "C": "user-shared", "D": "user-shared" },
              "dev": { "D": "user-named" }
            }
            """);

        EnvironmentSet environment = await LoadAsync(path, "dev");

        Assert.Equal(
            ("shared", "user-shared", "named", "user-named"),
            (Literal(environment, "A"), Literal(environment, "B"), Literal(environment, "C"),
                Literal(environment, "D")));
    }

    [Fact]
    public async Task LoadAsync_EnvironmentOnlyInUserFile_IsFound()
    {
        string path = await WriteEnvironmentFileAsync(UserScenarioEnvironmentFile);
        await WriteUserFileAsync("""{ "local": { "Path": "/local" } }""");

        EnvironmentSet environment = await LoadAsync(path, "local");

        Assert.Equal(("/local", "local"), (Literal(environment, "Path"), environment.SelectedName));
    }

    [Fact]
    public async Task LoadAsync_ProviderObjectInUserFile_ReturnsProviderValue()
    {
        string captureDirectory = Path.Combine(
            VisualStudioCapture.DirectoryPath,
            VisualStudioCapture.UserEnvironmentFolder);
        string path = Path.Combine(captureDirectory, EnvironmentFileLocator.FileName);

        EnvironmentSet environment = await EnvironmentLoader.LoadAsync(
            path,
            "dev",
            Path.Combine(captureDirectory, "env.http"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(environment.TryGetValue("Token", out EnvironmentValue? value));
        ProviderEnvironmentValue provider = Assert.IsType<ProviderEnvironmentValue>(value);
        Assert.Equal(
            ("plain-value", "EntraAccessToken", "api://example/.default"),
            (Literal(environment, "Plain"), provider.Provider, (string?)provider.Definition["scope"]));
    }

    [Fact]
    public async Task LoadAsync_FilesWithEnvironments_ListsAvailableNamesSortedWithoutShared()
    {
        string path = await WriteEnvironmentFileAsync(
            """{ "staging": {}, "$shared": {}, "Dev": {}, "dev": {} }""");
        await WriteUserFileAsync("""{ "local": {}, "dev": {}, "$shared": {} }""");

        EnvironmentSet environment = await LoadAsync(path, environmentName: null);

        Assert.Equal(["Dev", "dev", "local", "staging"], environment.AvailableNames);
    }

    [Fact]
    public async Task LoadAsync_EnvironmentFile_SetsPathAndSelectedName()
    {
        string path = await WriteEnvironmentFileAsync(UserScenarioEnvironmentFile);

        EnvironmentSet environment = await LoadAsync(path, "remote");

        Assert.Equal((path, "remote"), (environment.EnvironmentFilePath, environment.SelectedName));
    }

    [Fact]
    public async Task LoadAsync_VariableNameDifferingByCase_IsNotFound()
    {
        string path = await WriteEnvironmentFileAsync(UserScenarioEnvironmentFile);

        EnvironmentSet environment = await LoadAsync(path, "dev");

        Assert.False(environment.TryGetValue("hostaddress", out _));
    }

    [Fact]
    public async Task LoadAsync_NoFileAndNoEnvironmentName_ReturnsNone()
    {
        EnvironmentSet environment = await LoadAsync(environmentFilePath: null, environmentName: null);

        Assert.Same(EnvironmentSet.None, environment);
    }

    [Fact]
    public void None_HasNoFileNameOrValues()
    {
        EnvironmentSet none = EnvironmentSet.None;

        Assert.Equal(
            (null, null, 0, false),
            (none.EnvironmentFilePath, none.SelectedName, none.AvailableNames.Count, none.TryGetValue("A", out _)));
    }

    [Fact]
    public async Task LoadAsync_EnvironmentNameButNoFile_Throws()
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => LoadAsync(environmentFilePath: null, "dev"));

        Assert.Equal(
            $"--env dev was given but no http-client.env.json was found above {HttpFilePath}.",
            exception.Message);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "secretName": "name" }""")]
    [InlineData("""{ "provider": 1 }""")]
    [InlineData("""{ "provider": null }""")]
    [InlineData("""{ "Provider": "AzureKeyVault" }""")]
    public async Task LoadAsync_UnsupportedValue_ThrowsWithVariableAndFile(string json)
    {
        string path = await WriteEnvironmentFileAsync($$"""{ "dev": { "Bad": {{json}} } }""");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => LoadAsync(path, "dev"));

        Assert.Equal($"Variable 'Bad' in {path} has an unsupported value.", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_UnsupportedValueInUserFile_ThrowsWithUserFile()
    {
        string path = await WriteEnvironmentFileAsync(UserScenarioEnvironmentFile);
        string userPath = await WriteUserFileAsync("""{ "dev": { "Path": ["a"] } }""");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => LoadAsync(path, "dev"));

        Assert.Equal($"Variable 'Path' in {userPath} has an unsupported value.", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_UnsupportedValueInOtherEnvironment_IsIgnored()
    {
        string path = await WriteEnvironmentFileAsync(
            """{ "dev": { "A": "1" }, "remote": { "A": [1] }, "broken": 5 }""");

        EnvironmentSet environment = await LoadAsync(path, "dev");

        Assert.Equal("1", Literal(environment, "A"));
    }

    [Theory]
    [InlineData("\"text\"")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task LoadAsync_EnvironmentIsNotObject_ThrowsWithEnvironmentAndFile(string json)
    {
        string path = await WriteEnvironmentFileAsync($$"""{ "dev": {{json}}, "other": {} }""");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => LoadAsync(path, "dev"));

        Assert.Equal($"Environment 'dev' in {path} has an unsupported value.", exception.Message);
    }

    [Theory]
    [InlineData("dev")]
    [InlineData(null)]
    public async Task LoadAsync_SharedIsNotObject_ThrowsWithSharedAndFile(string? environmentName)
    {
        string path = await WriteEnvironmentFileAsync("""{ "$shared": 5, "dev": {} }""");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => LoadAsync(path, environmentName));

        Assert.Equal($"Environment '$shared' in {path} has an unsupported value.", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_ThrowsWithFileAndLineWithoutContent()
    {
        string path = await WriteEnvironmentFileAsync("{\n  \"dev\": {\n    \"Key\": fake-secret-123\n  }\n}");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => LoadAsync(path, "dev"));

        Assert.Equal($"Environment file {path} is not valid JSON (line 3).", exception.Message);
        Assert.IsType<JsonException>(exception.InnerException, exactMatch: false);
    }

    [Fact]
    public async Task LoadAsync_InvalidJsonInUserFile_ThrowsWithUserFile()
    {
        string path = await WriteEnvironmentFileAsync(UserScenarioEnvironmentFile);
        string userPath = await WriteUserFileAsync("{ \"dev\": ");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => LoadAsync(path, "dev"));

        Assert.Equal($"Environment file {userPath} is not valid JSON (line 1).", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_DuplicateVariable_ThrowsInvalidJson()
    {
        string path = await WriteEnvironmentFileAsync("{\n  \"dev\": {\n    \"A\": \"1\",\n    \"A\": \"2\"\n  }\n}");

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => LoadAsync(path, "dev"));

        Assert.Equal($"Environment file {path} is not valid JSON.", exception.Message);
        Assert.IsType<JsonException>(exception.InnerException, exactMatch: false);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public async Task LoadAsync_RootIsNotObject_ThrowsWithFile(string content)
    {
        string path = await WriteEnvironmentFileAsync(content);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => LoadAsync(path, environmentName: null));

        Assert.Equal($"Environment file {path} must contain a JSON object.", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_Utf8ByteOrderMark_Parses()
    {
        string path = Path.Combine(_directory.DirectoryPath, EnvironmentFileLocator.FileName);
        await File.WriteAllTextAsync(
            path,
            "{ \"dev\": { \"City\": \"caf\u00E9\" } }",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            TestContext.Current.CancellationToken);

        EnvironmentSet environment = await LoadAsync(path, "dev");

        Assert.Equal("caf\u00E9", Literal(environment, "City"));
    }

    [Theory]
    [InlineData("{ \"dev\": { \"City\": \"caf", "\" } }")]
    [InlineData("{ \"caf", "\": { \"City\": \"x\" } }")]
    public async Task LoadAsync_InvalidUtf8_ThrowsInvalidJson(string before, string after)
    {
        const byte Windows1252EAcute = 0xE9;
        string path = Path.Combine(_directory.DirectoryPath, EnvironmentFileLocator.FileName);
        byte[] content = [.. Encoding.ASCII.GetBytes(before), Windows1252EAcute, .. Encoding.ASCII.GetBytes(after)];
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => LoadAsync(path, environmentName: null));

        Assert.Equal($"Environment file {path} is not valid JSON.", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_CancelledToken_Throws()
    {
        string path = await WriteEnvironmentFileAsync(UserScenarioEnvironmentFile);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => EnvironmentLoader.LoadAsync(
                path,
                "dev",
                HttpFilePath,
                cancellationToken: new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task LoadAsync_UserFile_LogsItsPath()
    {
        string path = await WriteEnvironmentFileAsync(UserScenarioEnvironmentFile);
        await WriteUserFileAsync(UserScenarioUserFile);
        List<string> lines = [];

        await EnvironmentLoader.LoadAsync(
            path,
            "dev",
            HttpFilePath,
            lines.Add,
            TestContext.Current.CancellationToken);

        Assert.Equal([$"Environment user file: {path}.user"], lines);
    }

    [Fact]
    public async Task LoadAsync_NoUserFile_LogsNothing()
    {
        string path = await WriteEnvironmentFileAsync(UserScenarioEnvironmentFile);
        List<string> lines = [];

        await EnvironmentLoader.LoadAsync(
            path,
            "dev",
            HttpFilePath,
            lines.Add,
            TestContext.Current.CancellationToken);

        Assert.Empty(lines);
    }

    private static string? Literal(EnvironmentSet environment, string name) =>
        environment.TryGetValue(name, out EnvironmentValue? value) && value is LiteralEnvironmentValue literal
            ? literal.Value
            : null;

    private Task<EnvironmentSet> LoadAsync(string? environmentFilePath, string? environmentName) =>
        EnvironmentLoader.LoadAsync(
            environmentFilePath,
            environmentName,
            HttpFilePath,
            cancellationToken: TestContext.Current.CancellationToken);

    private Task<string> WriteEnvironmentFileAsync(string content) =>
        _directory.WriteFileAsync(EnvironmentFileLocator.FileName, content, TestContext.Current.CancellationToken);

    private Task<string> WriteUserFileAsync(string content) =>
        _directory.WriteFileAsync(UserFileName, content, TestContext.Current.CancellationToken);
}
