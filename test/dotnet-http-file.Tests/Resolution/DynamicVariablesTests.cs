using System.Globalization;
using DotNetHttpFile.Environments;
using DotNetHttpFile.Resolution;
using DotNetHttpFile.Templates;
using Microsoft.Extensions.Time.Testing;

namespace DotNetHttpFile.Tests.Resolution;

public sealed class DynamicVariablesTests
{
    private const int Seed = 42;
    private const string DotEnvPath = "/project/.env";

    private static readonly DateTimeOffset s_now = new(2024, 1, 17, 22, 59, 55, TimeSpan.Zero);

    private static readonly TimeSpan s_fraction = TimeSpan.FromTicks(5345770);

    private static readonly TimeZoneInfo s_localTimeZone = TimeZoneInfo.CreateCustomTimeZone(
        "UTC-08",
        TimeSpan.FromHours(-8),
        "UTC-08",
        "UTC-08");

    [Fact]
    public async Task EvaluateAsync_DatetimeIso8601_ReturnsRoundTripUtc()
    {
        string value = await EvaluateAsync("datetime", "iso8601");

        Assert.Equal("2024-01-17T22:59:55.5345770+00:00", value);
    }

    [Fact]
    public async Task EvaluateAsync_DatetimeWithoutArguments_ReturnsRoundTripUtc()
    {
        string value = await EvaluateAsync("datetime");

        Assert.Equal("2024-01-17T22:59:55.5345770+00:00", value);
    }

    [Fact]
    public async Task EvaluateAsync_DatetimeRfc1123_ReturnsGmt()
    {
        string value = await EvaluateAsync("datetime", "rfc1123");

        Assert.Equal("Wed, 17 Jan 2024 22:59:55 GMT", value);
    }

    [Fact]
    public async Task EvaluateAsync_DatetimeRfc1123WithOffset_AppliesOffset()
    {
        string value = await EvaluateAsync("datetime", "rfc1123", "1", "d");

        Assert.Equal("Thu, 18 Jan 2024 22:59:55 GMT", value);
    }

    [Fact]
    public async Task EvaluateAsync_LocalDatetimeIso8601_ReturnsRoundTripWithLocalOffset()
    {
        string value = await EvaluateAsync("localDatetime", "iso8601");

        Assert.Equal("2024-01-17T14:59:55.5345770-08:00", value);
    }

    [Fact]
    public async Task EvaluateAsync_LocalDatetimeWithoutArguments_ReturnsRoundTripWithLocalOffset()
    {
        string value = await EvaluateAsync("localDatetime");

        Assert.Equal("2024-01-17T14:59:55.5345770-08:00", value);
    }

    [Theory]
    [InlineData("datetime", "-1")]
    [InlineData("datetime", "1")]
    [InlineData("localDatetime", "+2")]
    public async Task EvaluateAsync_DatetimeOffsetWithoutUnit_Throws(string function, string amount)
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync(function, amount));

        Assert.Equal(
            $"Invalid arguments for '${function}'. Expected '{{{{${function} [format] [offset]}}}}'.",
            exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_LocalDatetimeRfc1123_UsesDocsFormat()
    {
        string value = await EvaluateAsync("localDatetime", "rfc1123");

        Assert.Equal("Wed, 17 Jan 2024 14:59:55 -08", value);
    }

    [Fact]
    public async Task EvaluateAsync_LocalDatetimeCustomFormatWithOffset_AppliesOffsetToLocalTime()
    {
        string value = await EvaluateAsync("localDatetime", "yyyy-MM-dd HH:mm", "10", "h");

        Assert.Equal("2024-01-18 00:59", value);
    }

    [Fact]
    public async Task EvaluateAsync_DatetimeCustomFormatWithOffset_AppliesOffset()
    {
        string value = await EvaluateAsync("datetime", "dd-MM-yyyy", "-1", "y");

        Assert.Equal("17-01-2023", value);
    }

    [Fact]
    public async Task EvaluateAsync_DatetimeOffsetWithoutFormat_AppliesOffset()
    {
        string value = await EvaluateAsync("datetime", "-1", "y");

        Assert.Equal("2023-01-17T22:59:55.5345770+00:00", value);
    }

    [Fact]
    public async Task EvaluateAsync_DatetimeCustomFormat_UsesInvariantCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            string value = await EvaluateAsync("datetime", "dddd MMMM");

            Assert.Equal("Wednesday January", value);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("ms", "2024-01-17T22:59:55.5355770+00:00")]
    [InlineData("s", "2024-01-17T22:59:56.5345770+00:00")]
    [InlineData("m", "2024-01-17T23:00:55.5345770+00:00")]
    [InlineData("h", "2024-01-17T23:59:55.5345770+00:00")]
    [InlineData("d", "2024-01-18T22:59:55.5345770+00:00")]
    [InlineData("w", "2024-01-24T22:59:55.5345770+00:00")]
    [InlineData("M", "2024-02-17T22:59:55.5345770+00:00")]
    [InlineData("y", "2025-01-17T22:59:55.5345770+00:00")]
    public async Task EvaluateAsync_DatetimeOffsetUnit_AddsOneUnit(string unit, string expected)
    {
        string value = await EvaluateAsync("datetime", "iso8601", "1", unit);

        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("1.5", "d")]
    [InlineData("one", "d")]
    [InlineData("1", "D")]
    [InlineData("1", "years")]
    public async Task EvaluateAsync_DatetimeInvalidOffset_Throws(string amount, string unit)
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("datetime", "iso8601", amount, unit));

        Assert.Equal(
            $"Invalid offset '{amount} {unit}' for '$datetime'. "
            + "Expected an integer and one of ms, s, m, h, d, w, M, y.",
            exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_DatetimeOffsetBeyondSupportedDates_Throws()
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("datetime", "iso8601", "2147483647", "y"));

        Assert.Equal("Offset '2147483647 y' for '$datetime' is out of range.", exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_DatetimeTooManyArguments_Throws()
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("datetime", "iso8601", "1", "d", "extra"));

        Assert.Equal(
            "Invalid arguments for '$datetime'. Expected '{{$datetime [format] [offset]}}'.",
            exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_DatetimeInvalidCustomFormat_Throws()
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("datetime", "j"));

        Assert.Equal("Invalid date format 'j' for '$datetime'.", exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_Timestamp_ReturnsUnixSeconds()
    {
        string value = await EvaluateAsync("timestamp");

        Assert.Equal("1705532395", value);
    }

    [Fact]
    public async Task EvaluateAsync_TimestampMinusOneDay_ReturnsUnixSeconds()
    {
        string value = await EvaluateAsync("timestamp", "-1", "d");

        Assert.Equal("1705445995", value);
    }

    [Fact]
    public async Task EvaluateAsync_TimestampPlusOneYear_ReturnsUnixSeconds()
    {
        string value = await EvaluateAsync("timestamp", "1", "y");

        Assert.Equal("1737154795", value);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("iso8601", "1", "y")]
    public async Task EvaluateAsync_TimestampWrongArgumentCount_Throws(params string[] arguments)
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("timestamp", arguments));

        Assert.Equal(
            "Invalid arguments for '$timestamp'. Expected '{{$timestamp [offset]}}'.",
            exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_TimestampInvalidOffset_Throws()
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("timestamp", "x", "y"));

        Assert.Equal(
            "Invalid offset 'x y' for '$timestamp'. Expected an integer and one of ms, s, m, h, d, w, M, y.",
            exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_RandomIntWithoutArguments_ReturnsNextFromRandom()
    {
        string expected = new Random(Seed).Next().ToString(CultureInfo.InvariantCulture);

        string value = await EvaluateAsync("randomInt");

        Assert.Equal(expected, value);
    }

    [Fact]
    public async Task EvaluateAsync_RandomIntWithRange_StaysInRange()
    {
        DynamicVariables variables = CreateVariables();
        DynamicSegment segment = new("randomInt", ["1", "4"]);
        List<int> values = [];

        for (int iteration = 0; iteration < 200; iteration++)
        {
            string value = await variables.EvaluateAsync(segment, TestContext.Current.CancellationToken);
            values.Add(int.Parse(value, CultureInfo.InvariantCulture));
        }

        Assert.Equal([1, 2, 3], values.Distinct().Order());
    }

    [Fact]
    public async Task EvaluateAsync_RandomIntNegativeRange_StaysInRange()
    {
        string value = await EvaluateAsync("randomInt", "-5", "-4");

        Assert.Equal("-5", value);
    }

    [Fact]
    public async Task EvaluateAsync_RandomIntEqualBounds_ReturnsMin()
    {
        string value = await EvaluateAsync("randomInt", "7", "7");

        Assert.Equal("7", value);
    }

    [Fact]
    public async Task EvaluateAsync_RandomIntMinAboveMax_Throws()
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("randomInt", "9", "1"));

        Assert.Equal("Invalid range for '$randomInt': min 9 is greater than max 1.", exception.Message);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("1", "2", "3")]
    public async Task EvaluateAsync_RandomIntWrongArgumentCount_Throws(params string[] arguments)
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("randomInt", arguments));

        Assert.Equal(
            "Invalid arguments for '$randomInt'. Expected '{{$randomInt [min max]}}'.",
            exception.Message);
    }

    [Theory]
    [InlineData("a", "5", "a")]
    [InlineData("1", "2.5", "2.5")]
    [InlineData("1", "99999999999", "99999999999")]
    public async Task EvaluateAsync_RandomIntNonIntegerArgument_Throws(string min, string max, string invalid)
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("randomInt", min, max));

        Assert.Equal($"Invalid integer '{invalid}' for '$randomInt'.", exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_Guid_ReturnsGuidInDFormat()
    {
        string value = await EvaluateAsync("guid");

        Assert.True(Guid.TryParseExact(value, "D", out _), value);
    }

    [Fact]
    public async Task EvaluateAsync_GuidWithArguments_Throws()
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("guid", "N"));

        Assert.Equal("Invalid arguments for '$guid'. Expected '{{$guid}}'.", exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_ProcessEnvValue_ReturnsValue()
    {
        string value = await EvaluateAsync("processEnv", "USERNAME");

        Assert.Equal("userFromProcess", value);
    }

    [Fact]
    public async Task EvaluateAsync_ProcessEnvMissing_Throws()
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("processEnv", "MISSING"));

        Assert.Equal("Environment variable 'MISSING' is not set.", exception.Message);
    }

    [Theory]
    [InlineData]
    [InlineData("A", "B")]
    public async Task EvaluateAsync_ProcessEnvWrongArgumentCount_Throws(params string[] arguments)
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("processEnv", arguments));

        Assert.Equal(
            "Invalid arguments for '$processEnv'. Expected '{{$processEnv NAME}}'.",
            exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_DotEnvValue_ReturnsValue()
    {
        string value = await EvaluateAsync("dotenv", "USERNAME");

        Assert.Equal("userFromDotenv", value);
    }

    [Fact]
    public async Task EvaluateAsync_DotEnvMissing_Throws()
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => EvaluateAsync("dotenv", "MISSING"));

        Assert.Equal($"'MISSING' is not defined in {DotEnvPath}.", exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_DotEnvFileNotFound_ThrowsNamingThePath()
    {
        DynamicVariables variables = new(
            TimeProvider.System,
            new Random(Seed),
            _ => null,
            _ => Task.FromResult(new DotEnvFile(DotEnvPath)));

        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(
            () => variables.EvaluateAsync(
                new DynamicSegment("dotenv", ["USERNAME"]),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            $"'USERNAME' cannot be read with $dotenv: file {DotEnvPath} was not found.",
            exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_DotEnvWithoutName_Throws()
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => EvaluateAsync("dotenv"));

        Assert.Equal("Invalid arguments for '$dotenv'. Expected '{{$dotenv NAME}}'.", exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_DotEnv_PassesCancellationTokenToLoader()
    {
        using CancellationTokenSource cancellation = new();
        CancellationToken received = CancellationToken.None;
        DynamicVariables variables = new(
            TimeProvider.System,
            new Random(Seed),
            _ => null,
            cancellationToken =>
            {
                received = cancellationToken;

                return Task.FromResult(new DotEnvFile(DotEnvPath)
                {
                    Values = new Dictionary<string, string> { ["A"] = "1" },
                });
            });

        await variables.EvaluateAsync(new DynamicSegment("dotenv", ["A"]), cancellation.Token);

        Assert.Equal(cancellation.Token, received);
    }

    [Fact]
    public async Task EvaluateAsync_FunctionOtherThanDotEnv_DoesNotLoadDotEnv()
    {
        DynamicVariables variables = new(
            TimeProvider.System,
            new Random(Seed),
            _ => null,
            _ => throw new InvalidOperationException());

        string value = await variables.EvaluateAsync(
            new DynamicSegment("randomInt", ["3", "3"]),
            TestContext.Current.CancellationToken);

        Assert.Equal("3", value);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("DATETIME")]
    public async Task EvaluateAsync_UnknownFunction_Throws(string function)
    {
        HttpFileException exception = await Assert.ThrowsAsync<HttpFileException>(() => EvaluateAsync(function));

        Assert.Equal($"Unknown dynamic variable '${function}'.", exception.Message);
    }

    private static Task<string> EvaluateAsync(string function, params string[] arguments) =>
        CreateVariables().EvaluateAsync(
            new DynamicSegment(function, arguments),
            TestContext.Current.CancellationToken);

    private static DynamicVariables CreateVariables()
    {
        FakeTimeProvider timeProvider = new(s_now + s_fraction);
        timeProvider.SetLocalTimeZone(s_localTimeZone);

        return new DynamicVariables(
            timeProvider,
            new Random(Seed),
            name => name == "USERNAME" ? "userFromProcess" : null,
            _ => Task.FromResult(new DotEnvFile(DotEnvPath)
            {
                Values = new Dictionary<string, string> { ["USERNAME"] = "userFromDotenv" },
            }));
    }
}
