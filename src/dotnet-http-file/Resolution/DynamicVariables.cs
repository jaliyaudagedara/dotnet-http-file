using System.Globalization;
using DotNetHttpFile.Environments;
using DotNetHttpFile.Templates;

namespace DotNetHttpFile.Resolution;

internal sealed class DynamicVariables(
    TimeProvider timeProvider,
    Random random,
    Func<string, string?> getEnvironmentVariable,
    Func<CancellationToken, Task<DotEnvFile>> loadDotEnv)
{
    private const string ProcessEnvFunction = "processEnv";
    private const string DotEnvFunction = "dotenv";
    private const string RandomIntFunction = "randomInt";
    private const string DateTimeFunction = "datetime";
    private const string LocalDateTimeFunction = "localDatetime";
    private const string TimestampFunction = "timestamp";
    private const string GuidFunction = "guid";

    private const string ProcessEnvSyntax = "$processEnv NAME";
    private const string DotEnvSyntax = "$dotenv NAME";
    private const string RandomIntSyntax = "$randomInt [min max]";
    private const string DateTimeSyntax = "$datetime [format] [offset]";
    private const string LocalDateTimeSyntax = "$localDatetime [format] [offset]";
    private const string TimestampSyntax = "$timestamp [offset]";
    private const string GuidSyntax = "$guid";

    private const string Rfc1123 = "rfc1123";
    private const string Iso8601 = "iso8601";
    private const string RoundTripFormat = "o";
    private const string Rfc1123Format = "r";
    private const string LocalRfc1123Format = "ddd, dd MMM yyyy HH:mm:ss zz";
    private const int DaysPerWeek = 7;

    /// <summary>
    /// Produces the value of a <c>{{$function arguments}}</c> expression.
    /// </summary>
    /// <exception cref="HttpFileException">
    /// The function is unknown, its arguments are invalid, or the variable it names is not defined.
    /// </exception>
    public async Task<string> EvaluateAsync(DynamicSegment segment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segment);

        IReadOnlyList<string> arguments = segment.Arguments;

        return segment.Function switch
        {
            ProcessEnvFunction => GetProcessEnv(arguments),
            DotEnvFunction => await GetDotEnvAsync(arguments, cancellationToken).ConfigureAwait(false),
            RandomIntFunction => GetRandomInt(arguments),
            DateTimeFunction => GetDateTime(DateTimeFunction, DateTimeSyntax, arguments, isLocal: false),
            LocalDateTimeFunction => GetDateTime(LocalDateTimeFunction, LocalDateTimeSyntax, arguments, isLocal: true),
            TimestampFunction => GetTimestamp(arguments),
            GuidFunction => GetGuid(arguments),
            _ => throw new HttpFileException(Messages.Format(Resources.UnknownDynamicVariable, segment.Function)),
        };
    }

    private static string GetGuid(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 0)
        {
            throw InvalidArguments(GuidFunction, GuidSyntax);
        }

        return Guid.NewGuid().ToString();
    }

    private static string RequireName(string function, string syntax, IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 1)
        {
            throw InvalidArguments(function, syntax);
        }

        return arguments[0];
    }

    private static int ParseInteger(string function, string text)
    {
        if (!TryParseInteger(text, out int value))
        {
            throw new HttpFileException(Messages.Format(Resources.InvalidDynamicVariableInteger, function, text));
        }

        return value;
    }

    private static bool TryParseInteger(string text, out int value) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private static DateTimeOffset ApplyOffset(string function, DateTimeOffset value, string amountText, string unit)
    {
        if (!TryParseInteger(amountText, out int amount))
        {
            throw InvalidOffset(function, amountText, unit);
        }

        try
        {
            return unit switch
            {
                "ms" => value.AddMilliseconds(amount),
                "s" => value.AddSeconds(amount),
                "m" => value.AddMinutes(amount),
                "h" => value.AddHours(amount),
                "d" => value.AddDays(amount),
                "w" => value.AddDays((double)amount * DaysPerWeek),
                "M" => value.AddMonths(amount),
                "y" => value.AddYears(amount),
                _ => throw InvalidOffset(function, amountText, unit),
            };
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new HttpFileException(
                Messages.Format(Resources.DynamicVariableOffsetOutOfRange, function, amountText, unit),
                exception);
        }
    }

    private static HttpFileException InvalidOffset(string function, string amountText, string unit) =>
        new(Messages.Format(Resources.InvalidDynamicVariableOffset, function, amountText, unit));

    private static HttpFileException InvalidArguments(string function, string syntax) =>
        new(Messages.Format(Resources.InvalidDynamicVariableArguments, function, syntax));

    private string GetProcessEnv(IReadOnlyList<string> arguments)
    {
        string name = RequireName(ProcessEnvFunction, ProcessEnvSyntax, arguments);

        return getEnvironmentVariable(name)
            ?? throw new HttpFileException(Messages.Format(Resources.EnvironmentVariableNotSet, name));
    }

    private async Task<string> GetDotEnvAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        string name = RequireName(DotEnvFunction, DotEnvSyntax, arguments);
        DotEnvFile dotEnv = await loadDotEnv(cancellationToken).ConfigureAwait(false);

        if (dotEnv.Values is null)
        {
            throw new HttpFileException(Messages.Format(Resources.DotEnvFileNotFound, name, dotEnv.Path));
        }

        if (!dotEnv.Values.TryGetValue(name, out string? value))
        {
            throw new HttpFileException(Messages.Format(Resources.DotEnvVariableNotDefined, name, dotEnv.Path));
        }

        return value;
    }

    private string GetRandomInt(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return random.Next().ToString(CultureInfo.InvariantCulture);
        }

        if (arguments.Count != 2)
        {
            throw InvalidArguments(RandomIntFunction, RandomIntSyntax);
        }

        int min = ParseInteger(RandomIntFunction, arguments[0]);
        int max = ParseInteger(RandomIntFunction, arguments[1]);
        if (min > max)
        {
            throw new HttpFileException(Messages.Format(
                Resources.InvalidDynamicVariableRange,
                RandomIntFunction,
                arguments[0],
                arguments[1]));
        }

        return random.Next(min, max).ToString(CultureInfo.InvariantCulture);
    }

    // Quotes are gone by the time arguments arrive, so the count tells a format from an offset:
    // a format is one argument and an offset is two. A lone integer is an offset missing its unit.
    private string GetDateTime(string function, string syntax, IReadOnlyList<string> arguments, bool isLocal)
    {
        (string? format, int offsetIndex) = arguments.Count switch
        {
            0 => (null, -1),
            1 when !TryParseInteger(arguments[0], out _) => (arguments[0], -1),
            2 => (null, 0),
            3 => (arguments[0], 1),
            _ => throw InvalidArguments(function, syntax),
        };

        DateTimeOffset value = isLocal ? timeProvider.GetLocalNow() : timeProvider.GetUtcNow();
        if (offsetIndex >= 0)
        {
            value = ApplyOffset(function, value, arguments[offsetIndex], arguments[offsetIndex + 1]);
        }

        string dotNetFormat = format switch
        {
            null or Iso8601 => RoundTripFormat,
            Rfc1123 => isLocal ? LocalRfc1123Format : Rfc1123Format,
            _ => format,
        };

        try
        {
            return value.ToString(dotNetFormat, CultureInfo.InvariantCulture);
        }
        catch (FormatException exception)
        {
            throw new HttpFileException(
                Messages.Format(Resources.InvalidDynamicVariableDateFormat, function, format),
                exception);
        }
    }

    private string GetTimestamp(IReadOnlyList<string> arguments)
    {
        DateTimeOffset value = timeProvider.GetUtcNow();
        if (arguments.Count == 2)
        {
            value = ApplyOffset(TimestampFunction, value, arguments[0], arguments[1]);
        }
        else if (arguments.Count != 0)
        {
            throw InvalidArguments(TimestampFunction, TimestampSyntax);
        }

        return value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }
}
