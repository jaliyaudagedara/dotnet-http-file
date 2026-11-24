using System.Buffers;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using DotNetHttpFile.Output;
using DotNetHttpFile.Parsing;
using DotNetHttpFile.Resolution;

namespace DotNetHttpFile.Cli;

internal static class CommandLine
{
    private const string FileArgumentName = "file";
    private const string NameOptionName = "--name";
    private const string NameOptionAlias = "-n";
    private const string NameOptionHelpName = "name";
    private const string EnvOptionName = "--env";
    private const string EnvOptionAlias = "-e";
    private const string EnvOptionHelpName = "env";
    private const string VerboseOptionName = "--verbose";
    private const string VerboseOptionAlias = "-v";
    private const string ShowOptionName = "--show";
    private const string ShowFull = "full";
    private const string ShowBody = "body";
    private const string ShowHeaders = "headers";
    private const string ShowStatus = "status";
    private const string ShowValueSeparator = "|";
    private const string InsecureOptionName = "--insecure";
    private const string InsecureOptionAlias = "-k";
    private const string VarOptionName = "--var";
    private const string VarOptionHelpName = "name=value";
    private const char VarAssignment = '=';
    private const char OptionPrefix = '-';

    private static readonly string[] s_fileExtensions = [".http", ".rest"];

    private static readonly string[] s_showValues = [ShowFull, ShowBody, ShowHeaders, ShowStatus];

    // The parser accepts --option=value and --option:value, and the value may be a secret.
    private static readonly SearchValues<char> s_optionValueSeparators = SearchValues.Create("=:");

    /// <summary>
    /// Parses <paramref name="args"/> and calls <paramref name="run"/> with what they ask for.
    /// </summary>
    /// <returns>
    /// The exit code of <paramref name="run"/>; <see cref="ExitCodes.NotSent"/> when the arguments are not valid,
    /// with the reason on standard error; <see cref="ExitCodes.Success"/> for <c>--help</c> and <c>--version</c>.
    /// </returns>
    public static Task<int> InvokeAsync(
        string[] args,
        Func<RunOptions, CancellationToken, Task<int>> run,
        ConsoleStreams console,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(console);

        Argument<string> fileArgument = new(FileArgumentName)
        {
            Description = Resources.FileArgumentDescription,
        };
        fileArgument.Validators.Add(ValidateFile);

        Option<string?> nameOption = new(NameOptionName, NameOptionAlias)
        {
            Description = Resources.NameOptionDescription,
            HelpName = NameOptionHelpName,
        };
        Option<string?> envOption = new(EnvOptionName, EnvOptionAlias)
        {
            Description = Resources.EnvOptionDescription,
            HelpName = EnvOptionHelpName,
        };
        Option<bool> verboseOption = new(VerboseOptionName, VerboseOptionAlias)
        {
            Description = Resources.VerboseOptionDescription,
        };
        Option<string?> showOption = new(ShowOptionName)
        {
            Description = Resources.ShowOptionDescription,
            HelpName = string.Join(ShowValueSeparator, s_showValues),
        };
        showOption.AcceptOnlyFromAmong(StringComparer.OrdinalIgnoreCase, s_showValues);
        Option<bool> insecureOption = new(InsecureOptionName, InsecureOptionAlias)
        {
            Description = Resources.InsecureOptionDescription,
        };

        Option<string[]> varOption = new(VarOptionName)
        {
            Description = Resources.VarOptionDescription,
            HelpName = VarOptionHelpName,
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        varOption.Validators.Add(ValidateVariables);

        RootCommand rootCommand = new(Resources.CommandDescription)
        {
            fileArgument,
            nameOption,
            envOption,
            verboseOption,
            showOption,
            insecureOption,
            varOption,
        };
        rootCommand.SetAction((parseResult, actionCancellationToken) =>
        {
            RunOptions options = new(parseResult.GetRequiredValue(fileArgument))
            {
                RequestName = parseResult.GetValue(nameOption),
                EnvironmentName = parseResult.GetValue(envOption),
                ForcedMode = ShowMode(parseResult.GetValue(showOption)),
                Insecure = parseResult.GetValue(insecureOption),
                Variables = ToVariables(parseResult.GetResult(varOption)),
                Verbose = parseResult.GetValue(verboseOption),
            };

            return run(options, actionCancellationToken);
        });

        // Without response files, so that a request name or a path that starts with '@' is taken as written.
        ParserConfiguration parserConfiguration = new()
        {
            ResponseFileTokenReplacer = null,
        };
        ParseResult parseResult = rootCommand.Parse(args, parserConfiguration);

        // The library's own action for a parse error prints the help as well and exits 1, which here means
        // "the server answered with an error status".
        if (parseResult.Action is ParseErrorAction)
        {
            ResponseRenderer renderer = new(console, new SecretMask(), OutputMode.Full);

            // The parser takes an option it does not know for the file when it comes first, and then reports
            // the real file and everything after it as unmatched. Only the unknown option is the mistake.
            string? unknownOption = FindUnknownOption(parseResult.GetResult(fileArgument));
            if (unknownOption is not null)
            {
                renderer.RenderError(Messages.Format(Resources.UnrecognizedOption, unknownOption));

                return Task.FromResult(ExitCodes.NotSent);
            }

            foreach (ParseError error in parseResult.Errors)
            {
                renderer.RenderError(ErrorMessage(error, parseResult.UnmatchedTokens));
            }

            return Task.FromResult(ExitCodes.NotSent);
        }

        InvocationConfiguration configuration = new()
        {
            Output = console.Output.Profile.Out.Writer,
            Error = console.Error.Profile.Out.Writer,

            // The run reports its own failures, and Ctrl+C reaches it through the caller's token.
            EnableDefaultExceptionHandler = false,
            ProcessTerminationTimeout = null,
        };

        return parseResult.InvokeAsync(configuration, cancellationToken);
    }

    /// <summary>
    /// Returns the name of the option that stands where the file is expected, without any value attached to it,
    /// or null when that token is a file or not an option.
    /// </summary>
    private static string? FindUnknownOption(ArgumentResult? fileResult)
    {
        if (fileResult is null || fileResult.Tokens.Count != 1)
        {
            return null;
        }

        string token = fileResult.Tokens[0].Value;

        return token.StartsWith(OptionPrefix) && !File.Exists(token) ? OptionName(token) : null;
    }

    /// <summary>
    /// Returns the parser's message for <paramref name="error"/>, or, when it quotes an unknown option with a value
    /// attached, the unrecognized option message naming only the option.
    /// </summary>
    private static string ErrorMessage(ParseError error, IReadOnlyList<string> unmatchedTokens)
    {
        // The parser reports an unmatched token against the command and quotes it whole. ParseError carries only
        // the message and the symbol result, not the token, so the token is matched by its quoted form.
        if (error.SymbolResult is CommandResult)
        {
            foreach (string token in unmatchedTokens)
            {
                if (token.StartsWith(OptionPrefix)
                    && OptionName(token).Length < token.Length
                    && error.Message.Contains($"'{token}'", StringComparison.Ordinal))
                {
                    return Messages.Format(Resources.UnrecognizedOption, OptionName(token));
                }
            }
        }

        return error.Message;
    }

    private static OutputMode? ShowMode(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (string.Equals(value, ShowBody, StringComparison.OrdinalIgnoreCase))
        {
            return OutputMode.Body;
        }

        if (string.Equals(value, ShowHeaders, StringComparison.OrdinalIgnoreCase))
        {
            return OutputMode.Headers;
        }

        return string.Equals(value, ShowStatus, StringComparison.OrdinalIgnoreCase)
            ? OutputMode.Status
            : OutputMode.Full;
    }

    // The text after the first '=' is never shown: it can be a secret.
    private static void ValidateVariables(OptionResult result)
    {
        // The arity counts values across every occurrence, so a bare --var after a valid one would pass it.
        if (result.IdentifierTokenCount > result.Tokens.Count)
        {
            result.AddError(Resources.VarOptionNeedsNameValue);
        }

        foreach (Token token in result.Tokens)
        {
            int assignment = token.Value.IndexOf(VarAssignment);
            if (assignment <= 0)
            {
                result.AddError(Resources.VarOptionNeedsNameValue);
            }
            else if (!FileVariable.IsValidName(token.Value[..assignment]))
            {
                result.AddError(Messages.Format(Resources.VarOptionInvalid, token.Value[..assignment]));
            }
        }
    }

    private static Dictionary<string, string>? ToVariables(OptionResult? result)
    {
        if (result is null)
        {
            return null;
        }

        Dictionary<string, string> variables = [with(StringComparer.Ordinal)];
        foreach (Token token in result.Tokens)
        {
            int assignment = token.Value.IndexOf(VarAssignment);
            variables[token.Value[..assignment]] = token.Value[(assignment + 1)..];
        }

        return variables;
    }

    private static void ValidateFile(ArgumentResult result)
    {
        // A missing argument is reported by the parser itself.
        if (result.Tokens.Count != 1)
        {
            return;
        }

        string path = result.Tokens[0].Value;
        if (!File.Exists(path))
        {
            result.AddError(Messages.Format(Resources.FileNotFound, path));
        }
        else if (!s_fileExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            result.AddError(Messages.Format(Resources.FileExtensionNotSupported, path));
        }
    }

    private static string OptionName(string token)
    {
        int separator = token.AsSpan().IndexOfAny(s_optionValueSeparators);

        return separator < 0 ? token : token[..separator];
    }
}
