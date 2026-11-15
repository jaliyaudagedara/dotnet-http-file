using System.Text;
using System.Text.Json.Nodes;
using DotNetHttpFile.Environments;
using DotNetHttpFile.Execution;
using DotNetHttpFile.Parsing;
using DotNetHttpFile.Resolution;
using DotNetHttpFile.Secrets;

namespace DotNetHttpFile.Tests.Resolution;

public sealed class VariableResolverTests
{
    private const string FilePath = "test.http";
    private const string ProviderName = "Fake";
    private const string Secret = "s3cret-value";
    private const int Seed = 42;

    private static readonly string s_environmentFileDirectory = Path.Combine(Path.GetTempPath(), "dotnet-http-file");

    private static readonly string s_environmentFilePath =
        Path.Combine(s_environmentFileDirectory, "http-client.env.json");

    [Fact]
    public async Task ResolveAsync_FileVariableComposedOfEarlierVariables_Resolves()
    {
        HttpFile file = Parse(
            "@hostname=localhost",
            "@port=44320",
            "@host={{hostname}}:{{port}}",
            "GET https://{{host}}/api/search/tool");
        VariableResolver resolver = CreateResolver(file);

        string url = await ResolveUrlAsync(resolver, file.Requests[0]);

        Assert.Equal("https://localhost:44320/api/search/tool", url);
    }

    [Fact]
    public async Task ResolveRequestAsync_EscapedExpressionOfDefinedVariable_SendsBracesWithoutLookup()
    {
        HttpFile file = Parse(
            "@id=42",
            @"POST https://localhost/items/{{id}}?raw=\{{id}}",
            @"X-Template: \{{id}} {{id}}",
            "",
            @"{""template"": ""\{{id}}"", ""id"": {{id}}}");
        VariableResolver resolver = CreateResolver(file);

        ResolvedRequest resolved = await resolver.ResolveRequestAsync(
            file.Requests[0],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ("https://localhost/items/42?raw={{id}}", "{{id}} 42", """{"template": "{{id}}", "id": 42}"""),
            (resolved.DisplayUrl, Assert.Single(resolved.Headers).Value, resolved.Body));
    }

    [Fact]
    public async Task ResolveAsync_FileVariableHoldingEscape_InsertsBracesWithoutParsingAgain()
    {
        HttpFile file = Parse(
            "@secret=value",
            @"@raw=\{{secret}}",
            "GET https://localhost/?v={{raw}}");
        VariableResolver resolver = CreateResolver(file);

        string url = await ResolveUrlAsync(resolver, file.Requests[0]);

        Assert.Equal("https://localhost/?v={{secret}}", url);
    }

    [Fact]
    public async Task ResolveRequestAsync_UndefinedInBodyAfterEscapes_ReportsExpressionLine()
    {
        HttpFile file = Parse(
            "POST https://localhost/items",
            "",
            @"\{{a}}\{{b}}",
            "{{missing}}");
        VariableResolver resolver = CreateResolver(file);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => resolver.ResolveRequestAsync(file.Requests[0], TestContext.Current.CancellationToken));

        Assert.Equal("Variable 'missing' is not defined (line 4).", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_FileVariableOverridesEnvironment_UsesFileValue()
    {
        HttpFile file = Parse(
            "@HostAddress=https://contoso.com",
            "GET {{HostAddress}}/weatherforecast");
        EnvironmentSet environment = CreateEnvironment(
            "dev",
            ("HostAddress", new LiteralEnvironmentValue("https://localhost:7128")));
        VariableResolver resolver = CreateResolver(file, environment);

        string url = await ResolveUrlAsync(resolver, file.Requests[0]);

        Assert.Equal("https://contoso.com/weatherforecast", url);
    }

    [Fact]
    public async Task ResolveAsync_EnvironmentValue_UsedWhenNoFileVariable()
    {
        HttpFile file = Parse("GET {{HostAddress}}/weatherforecast");
        EnvironmentSet environment = CreateEnvironment(
            "dev",
            ("HostAddress", new LiteralEnvironmentValue("https://localhost:7128")));
        VariableResolver resolver = CreateResolver(file, environment);

        string url = await ResolveUrlAsync(resolver, file.Requests[0]);

        Assert.Equal("https://localhost:7128/weatherforecast", url);
    }

    [Fact]
    public async Task ResolveAsync_EnvironmentValueWithBraces_IsNotParsedAsTemplate()
    {
        HttpFile file = Parse("GET https://localhost/{{Pattern}}");
        EnvironmentSet environment = CreateEnvironment(
            "dev",
            ("Pattern", new LiteralEnvironmentValue("{{Other}}")),
            ("Other", new LiteralEnvironmentValue("value")));
        VariableResolver resolver = CreateResolver(file, environment);

        string url = await ResolveUrlAsync(resolver, file.Requests[0]);

        Assert.Equal("https://localhost/{{Other}}", url);
    }

    [Fact]
    public async Task ResolveAsync_VariableRedefinedBetweenRequests_EachRequestUsesItsOwnDefinition()
    {
        HttpFile file = Parse(
            "@host=first",
            "GET https://{{host}}/",
            "",
            "###",
            "@host=second",
            "GET https://{{host}}/");
        VariableResolver resolver = CreateResolver(file);

        string[] urls =
        [
            await ResolveUrlAsync(resolver, file.Requests[0]),
            await ResolveUrlAsync(resolver, file.Requests[1]),
        ];

        Assert.Equal(["https://first/", "https://second/"], urls);
    }

    [Fact]
    public async Task ResolveAsync_VariableRedefinedFromItself_UsesEarlierDefinition()
    {
        HttpFile file = Parse(
            "@base=https://localhost",
            "@base={{base}}/api",
            "GET {{base}}/items");
        VariableResolver resolver = CreateResolver(file);

        string url = await ResolveUrlAsync(resolver, file.Requests[0]);

        Assert.Equal("https://localhost/api/items", url);
    }

    [Fact]
    public async Task ResolveAsync_ProviderValue_ResolvedOnceAndMasked()
    {
        HttpFile file = Parse(
            "GET https://localhost/items?key={{ApiToken}}",
            "Authorization: Bearer {{ApiToken}}");
        FakeSecretProvider provider = new(Secret);
        SecretMask mask = new();
        VariableResolver resolver = CreateResolver(file, ProviderEnvironment("ApiToken"), provider, secretMask: mask);
        RequestDefinition request = file.Requests[0];

        string url = await ResolveUrlAsync(resolver, request);
        string header = await resolver.ResolveAsync(
            request.Headers[0].Value,
            request.Headers[0].Line,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            (1, "https://localhost/items?key=" + Secret, "Bearer " + Secret, "Bearer ***"),
            (provider.CallCount, url, header, mask.Apply(header)));
    }

    [Fact]
    public async Task ResolveAsync_ProviderValue_PassesVariableDefinitionAndEnvironmentFileDirectory()
    {
        HttpFile file = Parse("GET https://localhost/{{ApiToken}}");
        FakeSecretProvider provider = new(Secret);
        VariableResolver resolver = CreateResolver(file, ProviderEnvironment("ApiToken"), provider);

        await ResolveUrlAsync(resolver, file.Requests[0]);

        SecretRequest request = Assert.Single(provider.Requests);
        Assert.Equal(
            ("ApiToken", ProviderName, s_environmentFileDirectory),
            (request.VariableName, request.GetRequiredString(ProviderName, "provider"), request.EnvironmentFileDirectory));
    }

    [Fact]
    public async Task ResolveAsync_ProviderValueThroughFileVariable_IsMasked()
    {
        HttpFile file = Parse(
            "@authorization=Bearer {{ApiToken}}",
            "GET https://localhost/items",
            "Authorization: {{authorization}}");
        SecretMask mask = new();
        VariableResolver resolver = CreateResolver(
            file,
            ProviderEnvironment("ApiToken"),
            new FakeSecretProvider(Secret),
            secretMask: mask);

        ResolvedRequest resolved = await resolver.ResolveRequestAsync(
            file.Requests[0],
            TestContext.Current.CancellationToken);

        Assert.Equal("Bearer ***", mask.Apply(Assert.Single(resolved.Headers).Value));
    }

    [Fact]
    public async Task ResolveAsync_ProviderNotUsedByRequest_NeverCalled()
    {
        HttpFile file = Parse(
            "@authorization=Bearer {{ApiToken}}",
            "GET https://localhost/public",
            "",
            "###",
            "GET https://localhost/private",
            "Authorization: {{authorization}}");
        FakeSecretProvider provider = new(Secret);
        VariableResolver resolver = CreateResolver(file, ProviderEnvironment("ApiToken"), provider);

        await resolver.ResolveRequestAsync(file.Requests[0], TestContext.Current.CancellationToken);

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_ProviderValueWithoutEnvironmentFile_ThrowsInvalidOperationException()
    {
        HttpFile file = Parse("GET https://localhost/{{ApiToken}}");
        EnvironmentSet environment = new(
            environmentFilePath: null,
            selectedName: "dev",
            availableNames: ["dev"],
            values: new Dictionary<string, EnvironmentValue>(StringComparer.Ordinal)
            {
                ["ApiToken"] = ProviderValue(),
            });
        VariableResolver resolver = CreateResolver(file, environment, new FakeSecretProvider(Secret));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ResolveUrlAsync(resolver, file.Requests[0]));
    }

    [Fact]
    public async Task ResolveAsync_UndefinedWithoutEnvironment_SuggestsEnv()
    {
        HttpFile file = Parse("GET {{HostAddress}}/weatherforecast");
        EnvironmentSet environment = CreateEnvironment(selectedName: null);
        VariableResolver resolver = CreateResolver(file, environment);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveUrlAsync(resolver, file.Requests[0]));

        Assert.Equal(
            "Variable 'HostAddress' is not defined (line 1). No environment selected; available: dev, prod. "
            + "Pass --env <name>.",
            exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_UndefinedWithEnvironmentSelected_NamesVariableAndLine()
    {
        HttpFile file = Parse(
            "GET https://localhost/items",
            "X-Value: {{missing}}");
        VariableResolver resolver = CreateResolver(file, CreateEnvironment("dev"));

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => resolver.ResolveRequestAsync(file.Requests[0], TestContext.Current.CancellationToken));

        Assert.Equal("Variable 'missing' is not defined (line 2).", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_UndefinedWithoutEnvironmentFile_HasNoSuggestion()
    {
        HttpFile file = Parse("GET {{HostAddress}}/weatherforecast");
        VariableResolver resolver = CreateResolver(file);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveUrlAsync(resolver, file.Requests[0]));

        Assert.Equal("Variable 'HostAddress' is not defined (line 1).", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_UndefinedInsideFileVariable_ReportsVariableLine()
    {
        HttpFile file = Parse(
            "",
            "@host={{hostname}}:5000",
            "GET https://{{host}}/");
        VariableResolver resolver = CreateResolver(file);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveUrlAsync(resolver, file.Requests[0]));

        Assert.Equal("Variable 'hostname' is not defined (line 2).", exception.Message);
    }

    [Fact]
    public async Task ResolveAsync_VariableDefinedAfterRequest_IsNotDefined()
    {
        HttpFile file = Parse(
            "GET https://{{host}}/",
            "",
            "###",
            "@host=localhost",
            "GET https://{{host}}/");
        VariableResolver resolver = CreateResolver(file);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveUrlAsync(resolver, file.Requests[0]));

        Assert.Equal("Variable 'host' is not defined (line 1).", exception.Message);
    }

    // A file variable only sees definitions above it, so two variables that name each other never form a cycle:
    // the first one refers to a name that is not defined yet.
    [Fact]
    public async Task ResolveAsync_VariablesReferencingEachOther_ReportsFirstAsUndefined()
    {
        HttpFile file = Parse(
            "@a={{b}}",
            "@b={{a}}",
            "GET https://localhost/{{b}}");
        VariableResolver resolver = CreateResolver(file);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => ResolveUrlAsync(resolver, file.Requests[0]));

        Assert.Equal("Variable 'b' is not defined (line 1).", exception.Message);
    }

    [Fact]
    public async Task ResolveRequestAsync_UndefinedInBody_ReportsExpressionLine()
    {
        HttpFile file = Parse(
            "POST https://localhost/items",
            "Accept: */*",
            "",
            "{",
            "  \"id\": \"{{$guid}}\",",
            "  \"name\": \"{{missing}}\"",
            "}");
        VariableResolver resolver = CreateResolver(file);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => resolver.ResolveRequestAsync(file.Requests[0], TestContext.Current.CancellationToken));

        Assert.Equal("Variable 'missing' is not defined (line 6).", exception.Message);
    }

    [Fact]
    public async Task ResolveRequestAsync_FileVariableWithDynamicValue_ResolvedOncePerRun()
    {
        HttpFile file = Parse(
            "@id={{$guid}}",
            "PUT https://localhost/items/{{id}}",
            "X-Id: {{id}}",
            "",
            "{{id}}");
        VariableResolver resolver = CreateResolver(file);

        ResolvedRequest resolved = await resolver.ResolveRequestAsync(
            file.Requests[0],
            TestContext.Current.CancellationToken);

        string id = Assert.Single(resolved.Headers).Value;
        Assert.True(Guid.TryParse(id, out _));
        Assert.Equal(("https://localhost/items/" + id, id), (resolved.Url.AbsoluteUri, resolved.Body));
    }

    [Fact]
    public async Task ResolveRequestAsync_DynamicVariableUsedDirectly_EvaluatedAtEachUse()
    {
        HttpFile file = Parse(
            "GET https://localhost/items",
            "X-First: {{$guid}}",
            "X-Second: {{$guid}}");
        VariableResolver resolver = CreateResolver(file);

        ResolvedRequest resolved = await resolver.ResolveRequestAsync(
            file.Requests[0],
            TestContext.Current.CancellationToken);

        Assert.NotEqual(resolved.Headers[0].Value, resolved.Headers[1].Value);
    }

    [Fact]
    public async Task ResolveRequestAsync_RequestWithHeadersAndBody_ResolvesEveryPart()
    {
        HttpFile file = Parse(
            "@host=localhost:5000",
            "@kind=json",
            "POST https://{{host}}/items HTTP/2",
            "Content-Type: application/{{kind}}",
            "Accept: */*",
            "X-Host: {{host}}",
            "",
            "{",
            "  \"host\": \"{{host}}\"",
            "}");
        VariableResolver resolver = CreateResolver(file);

        ResolvedRequest resolved = await resolver.ResolveRequestAsync(
            file.Requests[0],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ("POST", "https://localhost:5000/items", "HTTP/2", "{\r\n  \"host\": \"localhost:5000\"\r\n}"),
            (resolved.Method, resolved.Url.AbsoluteUri, resolved.HttpVersion, resolved.Body));
        Assert.Equal(
            [
                KeyValuePair.Create("Content-Type", "application/json"),
                KeyValuePair.Create("Accept", "*/*"),
                KeyValuePair.Create("X-Host", "localhost:5000"),
            ],
            resolved.Headers);
    }

    [Fact]
    public async Task ResolveRequestAsync_RequestWithHeaders_HeaderLinesAreParallelToHeaders()
    {
        HttpFile file = Parse(
            "@host=localhost:5000",
            "",
            "GET https://{{host}}/items",
            "Accept: */*",
            "X-Host: {{host}}");
        VariableResolver resolver = CreateResolver(file);

        ResolvedRequest resolved = await resolver.ResolveRequestAsync(
            file.Requests[0],
            TestContext.Current.CancellationToken);

        Assert.Equal([4, 5], resolved.HeaderLines);
    }

    [Fact]
    public async Task ResolveRequestAsync_RequestWithoutBody_HasNullBody()
    {
        HttpFile file = Parse("GET https://localhost/items");
        VariableResolver resolver = CreateResolver(file);

        ResolvedRequest resolved = await resolver.ResolveRequestAsync(
            file.Requests[0],
            TestContext.Current.CancellationToken);

        Assert.Null(resolved.Body);
    }

    [Theory]
    [InlineData("/weatherforecast")]
    [InlineData("weatherforecast")]
    [InlineData("localhost:5000/weatherforecast")]
    [InlineData("ftp://localhost/weatherforecast")]
    [InlineData("file:///c:/weatherforecast")]
    public async Task ResolveRequestAsync_RelativeUrl_Throws(string url)
    {
        HttpFile file = Parse(
            "@target=" + url,
            "",
            "GET {{target}}");
        VariableResolver resolver = CreateResolver(file);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => resolver.ResolveRequestAsync(file.Requests[0], TestContext.Current.CancellationToken));

        Assert.Equal($"'{url}' is not an absolute http or https URL (line 3).", exception.Message);
    }

    [Fact]
    public async Task ResolveRequestAsync_InvalidUrlHoldingSecret_MasksSecretInMessage()
    {
        HttpFile file = Parse("GET {{ApiToken}}/items?key={{ApiToken}}");
        VariableResolver resolver = CreateResolver(file, ProviderEnvironment("ApiToken"), new FakeSecretProvider(Secret));

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => resolver.ResolveRequestAsync(file.Requests[0], TestContext.Current.CancellationToken));

        Assert.Equal("'***/items?key=***' is not an absolute http or https URL (line 1).", exception.Message);
    }

    [Fact]
    public async Task ResolveRequestAsync_UrlHoldingProviderValue_DisplayUrlIsMasked()
    {
        const string UrlSecret = "p@ss w/rd";
        HttpFile file = Parse("GET https://localhost/items?key={{ApiToken}}");
        VariableResolver resolver = CreateResolver(
            file,
            ProviderEnvironment("ApiToken"),
            new FakeSecretProvider(UrlSecret));

        ResolvedRequest resolved = await resolver.ResolveRequestAsync(
            file.Requests[0],
            TestContext.Current.CancellationToken);

        Assert.Equal("https://localhost/items?key=***", resolved.DisplayUrl);
        Assert.DoesNotContain("p@ss", resolved.DisplayUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("w/rd", resolved.DisplayUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveRequestAsync_UrlHoldingProviderValue_ToStringPrintsDisplayUrl()
    {
        HttpFile file = Parse(
            "POST https://localhost/items?key={{ApiToken}}",
            "",
            "{{ApiToken}}");
        VariableResolver resolver = CreateResolver(file, ProviderEnvironment("ApiToken"), new FakeSecretProvider(Secret));

        ResolvedRequest resolved = await resolver.ResolveRequestAsync(
            file.Requests[0],
            TestContext.Current.CancellationToken);

        Assert.Equal("POST https://localhost/items?key=***", resolved.ToString());
    }

    [Fact]
    public async Task ResolveRequestAsync_UrlWithoutSecrets_DisplayUrlIsResolvedText()
    {
        HttpFile file = Parse(
            "@host=LocalHost:5000",
            "GET https://{{host}}/items?q=a b");
        VariableResolver resolver = CreateResolver(file);

        ResolvedRequest resolved = await resolver.ResolveRequestAsync(
            file.Requests[0],
            TestContext.Current.CancellationToken);

        Assert.Equal("https://LocalHost:5000/items?q=a b", resolved.DisplayUrl);
    }

    [Fact]
    public async Task ResolveRequestAsync_RelativeUrlWithProviderHeader_FailsBeforeCallingProvider()
    {
        HttpFile file = Parse(
            "GET /items",
            "Authorization: Bearer {{ApiToken}}");
        FakeSecretProvider provider = new(Secret);
        VariableResolver resolver = CreateResolver(file, ProviderEnvironment("ApiToken"), provider);

        await Assert.ThrowsAsync<HttpFileException>(
            () => resolver.ResolveRequestAsync(file.Requests[0], TestContext.Current.CancellationToken));

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task ResolveRequestAsync_ResponseReference_ReadsStoredExchange()
    {
        HttpFile file = Parse(
            "# @name login",
            "POST https://localhost/login",
            "",
            "###",
            "@token={{login.response.body.$.token}}",
            "GET https://localhost/items",
            "Authorization: Bearer {{token}}");
        ExchangeStore exchanges = new();
        VariableResolver resolver = CreateResolver(file, exchanges: exchanges);
        ResolvedRequest login = await resolver.ResolveRequestAsync(
            file.Requests[0],
            TestContext.Current.CancellationToken);
        exchanges.Add("login", new HttpExchange(login, JsonResponse("""{"token": "abc123"}""")));

        ResolvedRequest resolved = await resolver.ResolveRequestAsync(
            file.Requests[1],
            TestContext.Current.CancellationToken);

        Assert.Equal("Bearer abc123", Assert.Single(resolved.Headers).Value);
    }

    [Fact]
    public async Task ResolveRequestAsync_RequestHeaderReferenceHoldingProviderValue_IsMasked()
    {
        HttpFile file = Parse(
            "# @name login",
            "POST https://localhost/login",
            "Authorization: Bearer {{ApiToken}}",
            "",
            "###",
            "GET https://localhost/items",
            "Authorization: {{login.request.headers.Authorization}}");
        ExchangeStore exchanges = new();
        SecretMask mask = new();
        VariableResolver resolver = CreateResolver(
            file,
            ProviderEnvironment("ApiToken"),
            new FakeSecretProvider(Secret),
            exchanges,
            mask);
        ResolvedRequest login = await resolver.ResolveRequestAsync(
            file.Requests[0],
            TestContext.Current.CancellationToken);
        exchanges.Add("login", new HttpExchange(login, JsonResponse("{}")));

        ResolvedRequest resolved = await resolver.ResolveRequestAsync(
            file.Requests[1],
            TestContext.Current.CancellationToken);

        string header = Assert.Single(resolved.Headers).Value;
        Assert.Equal(("Bearer " + Secret, "Bearer ***"), (header, mask.Apply(header)));
    }

    [Fact]
    public async Task ResolveRequestAsync_ReferenceToRequestNotSent_Throws()
    {
        HttpFile file = Parse(
            "# @name login",
            "POST https://localhost/login",
            "",
            "###",
            "GET https://localhost/items/{{login.response.body.$.id}}");
        VariableResolver resolver = CreateResolver(file);

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => resolver.ResolveRequestAsync(file.Requests[1], TestContext.Current.CancellationToken));

        Assert.Equal("Request 'login' has not been sent.", exception.Message);
    }

    private static HttpFile Parse(params string[] lines) =>
        HttpFileParser.Parse(FilePath, string.Join("\r\n", lines) + "\r\n");

    private static Task<string> ResolveUrlAsync(VariableResolver resolver, RequestDefinition request) =>
        resolver.ResolveAsync(request.Url, request.Line, TestContext.Current.CancellationToken);

    private static VariableResolver CreateResolver(
        HttpFile file,
        EnvironmentSet? environment = null,
        FakeSecretProvider? provider = null,
        ExchangeStore? exchanges = null,
        SecretMask? secretMask = null)
    {
        DynamicVariables dynamicVariables = new(
            TimeProvider.System,
            new Random(Seed),
            _ => null,
            _ => Task.FromResult(new DotEnvFile(".env", new Dictionary<string, string>())));

        return new VariableResolver(
            file,
            environment ?? EnvironmentSet.None,
            new SecretProviderRegistry(provider is null ? [] : [provider]),
            dynamicVariables,
            exchanges ?? new ExchangeStore(),
            secretMask ?? new SecretMask());
    }

    private static EnvironmentSet CreateEnvironment(
        string? selectedName,
        params (string Name, EnvironmentValue Value)[] values) =>
        new(
            s_environmentFilePath,
            selectedName,
            availableNames: ["dev", "prod"],
            values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal));

    private static EnvironmentSet ProviderEnvironment(string variableName) =>
        CreateEnvironment("dev", (variableName, ProviderValue()));

    private static ProviderEnvironmentValue ProviderValue() =>
        new(
            ProviderName,
            new JsonObject
            {
                ["provider"] = ProviderName,
            });

    private static ReceivedResponse JsonResponse(string body) =>
        new(
            new Version(1, 1),
            200,
            "OK",
            [KeyValuePair.Create("Content-Type", "application/json")],
            Encoding.UTF8.GetBytes(body),
            TimeSpan.Zero);

    private sealed class FakeSecretProvider(string secret) : ISecretProvider
    {
        public string Name => ProviderName;

        public int CallCount => Requests.Count;

        public List<SecretRequest> Requests { get; } = [];

        public Task<string> ResolveAsync(SecretRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Task.FromResult(secret);
        }
    }
}
