using System.Text;
using DotNetHttpFile.Environments;

namespace DotNetHttpFile.Tests.Environments;

public sealed class DotEnvFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dotnet-http-file-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task LoadAsync_QuotedValuesAndComments_Parses()
    {
        string path = await WriteAsync(
            "# a comment\r\n"
            + "\r\n"
            + "USERNAME=userFromDotenv\r\n"
            + "  SPACED  =  padded value  \r\n"
            + "DOUBLE=\"double quoted\"\r\n"
            + "SINGLE='single quoted'\r\n"
            + "   # an indented comment\r\n"
            + "EMPTY=\r\n");

        IReadOnlyDictionary<string, string> values = await LoadValuesAsync(path);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["USERNAME"] = "userFromDotenv",
                ["SPACED"] = "padded value",
                ["DOUBLE"] = "double quoted",
                ["SINGLE"] = "single quoted",
                ["EMPTY"] = string.Empty,
            },
            values);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsNoValuesAndThePath()
    {
        string path = Path.Combine(_directory, ".env");

        DotEnvFile dotEnv = await DotEnvFile.LoadAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(new DotEnvFile(path, Values: null), dotEnv);
    }

    [Fact]
    public async Task LoadAsync_MissingDirectory_ReturnsNoValues()
    {
        DotEnvFile dotEnv = await DotEnvFile.LoadAsync(
            Path.Combine(_directory, "missing", ".env"),
            TestContext.Current.CancellationToken);

        Assert.Null(dotEnv.Values);
    }

    [Fact]
    public async Task LoadAsync_ExistingFile_ReturnsThePath()
    {
        string path = await WriteAsync("A=1");

        DotEnvFile dotEnv = await DotEnvFile.LoadAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(path, dotEnv.Path);
    }

    [Fact]
    public async Task LoadAsync_LinesWithoutAssignmentOrKey_SkipsThem()
    {
        string path = await WriteAsync("JUSTAKEY\n=novalue\nA=1\n");

        IReadOnlyDictionary<string, string> values = await LoadValuesAsync(path);

        Assert.Equal(new Dictionary<string, string> { ["A"] = "1" }, values);
    }

    [Fact]
    public async Task LoadAsync_DuplicateKey_LastWins()
    {
        string path = await WriteAsync("A=1\nA=2\n");

        IReadOnlyDictionary<string, string> values = await LoadValuesAsync(path);

        Assert.Equal("2", values["A"]);
    }

    [Fact]
    public async Task LoadAsync_KeysDifferingByCase_KeepsBoth()
    {
        string path = await WriteAsync("name=lower\nNAME=upper\n");

        IReadOnlyDictionary<string, string> values = await LoadValuesAsync(path);

        Assert.Equal(("lower", "upper"), (values["name"], values["NAME"]));
    }

    [Fact]
    public async Task LoadAsync_ValueWithAssignmentCharacter_SplitsOnFirst()
    {
        string path = await WriteAsync("CONNECTION=Server=localhost;Port=5432");

        IReadOnlyDictionary<string, string> values = await LoadValuesAsync(path);

        Assert.Equal("Server=localhost;Port=5432", values["CONNECTION"]);
    }

    [Theory]
    [InlineData("\"mismatched'", "\"mismatched'")]
    [InlineData("\"", "\"")]
    [InlineData("\"\"", "")]
    [InlineData("\"\"nested\"\"", "\"nested\"")]
    [InlineData("\" padded \"", " padded ")]
    [InlineData("say \"hi\"", "say \"hi\"")]
    public async Task LoadAsync_SurroundingQuotes_RemovesOneMatchingPair(string written, string expected)
    {
        string path = await WriteAsync("A=" + written);

        IReadOnlyDictionary<string, string> values = await LoadValuesAsync(path);

        Assert.Equal(expected, values["A"]);
    }

    [Fact]
    public async Task LoadAsync_Utf8ByteOrderMarkAndMixedLineEndings_Parses()
    {
        string path = Path.Combine(_directory, ".env");
        await File.WriteAllTextAsync(
            path,
            "A=1\rB=2\nC=3\r\nD=caf\u00E9",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            TestContext.Current.CancellationToken);

        IReadOnlyDictionary<string, string> values = await LoadValuesAsync(path);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["A"] = "1",
                ["B"] = "2",
                ["C"] = "3",
                ["D"] = "caf\u00E9",
            },
            values);
    }

    [Fact]
    public async Task LoadAsync_CancelledToken_Throws()
    {
        string path = await WriteAsync("A=1");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DotEnvFile.LoadAsync(path, new CancellationToken(canceled: true)));
    }

    private static async Task<IReadOnlyDictionary<string, string>> LoadValuesAsync(string path)
    {
        DotEnvFile dotEnv = await DotEnvFile.LoadAsync(path, TestContext.Current.CancellationToken);

        return dotEnv.Values ?? throw new InvalidOperationException($"{path} does not exist.");
    }

    private async Task<string> WriteAsync(string content)
    {
        string path = Path.Combine(_directory, ".env");
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);

        return path;
    }
}
