using System.Text;
using DotNetHttpFile.Environments;
using DotNetHttpFile.Execution;
using DotNetHttpFile.Output;
using DotNetHttpFile.Parsing;
using DotNetHttpFile.Resolution;
using DotNetHttpFile.Secrets;

namespace DotNetHttpFile.Cli;

/// <summary>
/// Runs one request of an <c>.http</c> file, after the requests it refers to.
/// </summary>
/// <param name="console">The streams the run writes to.</param>
/// <param name="picker">Picks the request when none is named and a terminal can ask.</param>
/// <param name="createProviders">Creates the secret providers of a run, given the run's diagnostic log.</param>
/// <param name="createHandler">Creates the HTTP handler; the argument is true for <c>--insecure</c>.</param>
/// <param name="timeProvider">The clock of the dynamic variables.</param>
/// <param name="getEnvironmentVariable">Reads a process environment variable.</param>
internal sealed class HttpFileRunner(
    ConsoleStreams console,
    IRequestPicker picker,
    Func<DiagnosticLog, SecretProviderRegistry> createProviders,
    Func<bool, HttpMessageHandler> createHandler,
    TimeProvider timeProvider,
    Func<string, string?> getEnvironmentVariable)
{
    private const string DotEnvFileName = ".env";
    private const int UnauthorizedStatusCode = 401;
    private const string WrongTenantCode = "InvalidAuthenticationTokenTenant";

    private static readonly byte[] s_wrongTenantCodeUtf8 = Encoding.UTF8.GetBytes(WrongTenantCode);

    /// <summary>
    /// Sends the request <paramref name="options"/> selects and writes the outcome to the console.
    /// </summary>
    /// <returns>
    /// <see cref="ExitCodes.Success"/> for a 2xx response, <see cref="ExitCodes.ErrorStatus"/> for any other
    /// response or for one whose body could not be decoded, <see cref="ExitCodes.NotSent"/> when the request could
    /// not be sent and <see cref="ExitCodes.Cancelled"/> when <paramref name="cancellationToken"/> was cancelled.
    /// </returns>
    public async Task<int> RunAsync(RunOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        OutputMode mode = options.ForcedMode ?? (console.IsOutputRedirected ? OutputMode.Body : OutputMode.Full);
        SecretMask secretMask = new();
        ResponseRenderer renderer = new(console, secretMask, mode);
        DiagnosticLog log = new(console.Error, secretMask) { IsEnabled = options.Verbose };

        try
        {
            return await RunCoreAsync(options, secretMask, renderer, log, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            renderer.RenderError(Resources.Cancelled);

            return ExitCodes.Cancelled;
        }
        catch (HttpFileException) when (cancellationToken.IsCancellationRequested)
        {
            // A provider reports a sign-in that Ctrl+C interrupted as a failure of its own; it is still a cancel.
            renderer.RenderError(Resources.Cancelled);

            return ExitCodes.Cancelled;
        }
        catch (HttpFileException exception)
        {
            // The message alone: an inner exception can quote a file or a response.
            renderer.RenderError(exception.Message);

            return exception.ExitCode;
        }
        catch (Exception exception)
        {
            // A stack trace helps nobody at a prompt; the type and message are enough to report a bug.
            renderer.RenderError(Messages.Format(
                Resources.UnexpectedError,
                exception.GetType().Name,
                Detail(exception)));

            return ExitCodes.NotSent;
        }
    }

    // The message of an AggregateException quotes the messages of the exceptions it holds, which nothing has
    // vetted for display, so only their types are named.
    private static string Detail(Exception exception) =>
        exception is AggregateException aggregate
            ? string.Join(
                Messages.ListSeparator,
                aggregate.Flatten().InnerExceptions.Select(innerException => innerException.GetType().Name))
            : exception.Message;

    // Only for a call that does nothing but read files: an IOException from anything else is not a file error.
    private static async Task<T> ReadFilesAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception exception) when (FileReadFailure.IsFileReadFailure(exception))
        {
            throw FileReadFailure.Create(exception);
        }
    }

    private static async Task<DotEnvFile> LoadDotEnvAsync(
        string httpFilePath,
        DiagnosticLog log,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(httpFilePath);
        string httpFileDirectory = Path.GetDirectoryName(fullPath) ?? fullPath;
        string directory = ProjectLocator.FindProjectDirectory(httpFileDirectory) ?? httpFileDirectory;

        DotEnvFile dotEnv = await DotEnvFile.LoadAsync(Path.Combine(directory, DotEnvFileName), cancellationToken)
            .ConfigureAwait(false);

        // A missing file is not read; the $dotenv error names the path.
        if (dotEnv.Values is not null)
        {
            log.Write(Messages.Format(Resources.VerboseDotEnvFile, dotEnv.Path));
        }

        return dotEnv;
    }

    private async Task<int> RunCoreAsync(
        RunOptions options,
        SecretMask secretMask,
        ResponseRenderer renderer,
        DiagnosticLog log,
        CancellationToken cancellationToken)
    {
        string filePath = options.FilePath;
        if (!File.Exists(filePath))
        {
            throw new HttpFileException(Messages.Format(Resources.FileNotFound, filePath));
        }

        log.Write(Messages.Format(Resources.VerboseHttpFile, Path.GetFullPath(filePath)));
        HttpFile file = await ReadFilesAsync(() => HttpFileParser.LoadAsync(filePath, cancellationToken))
            .ConfigureAwait(false);

        string? environmentFilePath = EnvironmentFileLocator.Find(filePath);
        log.Write(environmentFilePath is null
            ? Resources.VerboseNoEnvironmentFile
            : Messages.Format(Resources.VerboseEnvironmentFile, environmentFilePath));
        EnvironmentSet environment = await ReadFilesAsync(() => EnvironmentLoader.LoadAsync(
            environmentFilePath,
            options.EnvironmentName,
            filePath,
            log.Write,
            cancellationToken)).ConfigureAwait(false);
        log.Write(environment.SelectedName is null
            ? Resources.VerboseNoEnvironment
            : Messages.Format(Resources.VerboseEnvironment, environment.SelectedName));

        RequestDefinition target = await SelectRequestAsync(file, options.RequestName, cancellationToken)
            .ConfigureAwait(false);
        WarnOfUnusedVariables(file, options.Variables, renderer);
        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, target, options.Variables);
        log.Write(Messages.Format(
            Resources.VerbosePlan,
            string.Join(
                Messages.ListSeparator,
                plan.Select(request => request.DisplayName))));

        ExchangeStore exchanges = new();
        VariableResolver resolver = new(
            file,
            environment,
            createProviders(log),
            CreateDynamicVariables(filePath, log),
            exchanges,
            secretMask,
            options.Variables);

        using HttpClient httpClient = ToolHttpClient.Create(createHandler(options.Insecure));
        RequestSender sender = new(httpClient, secretMask, renderer.RenderRedirect);

        foreach (RequestDefinition dependency in plan.Take(plan.Count - 1))
        {
            HttpExchange exchange = await SendAsync(dependency).ConfigureAwait(false);

            // The -v log already has a line for the exchange.
            if (!options.Verbose)
            {
                renderer.RenderDependency(dependency, exchange);
            }

            RenderWrongTenantHint(renderer, exchange);
            if (!exchange.Response.IsSuccess)
            {
                renderer.RenderError(Messages.Format(
                    Resources.DependencyFailed,
                    dependency.DisplayName,
                    ResponseRenderer.StatusText(exchange.Response),
                    target.DisplayName));

                return ExitCodes.ErrorStatus;
            }
        }

        HttpExchange targetExchange = await SendAsync(target).ConfigureAwait(false);
        await renderer.RenderAsync(targetExchange, cancellationToken).ConfigureAwait(false);
        RenderWrongTenantHint(renderer, targetExchange);

        return targetExchange.Response.IsSuccess ? ExitCodes.Success : ExitCodes.ErrorStatus;

        async Task<HttpExchange> SendAsync(RequestDefinition request)
        {
            ResolvedRequest resolved = GraphQLRequest.Apply(
                await resolver.ResolveRequestAsync(request, cancellationToken).ConfigureAwait(false),
                request.Line);

            List<string> warnings = [];
            ReceivedResponse response;
            try
            {
                response = await sender
                    .SendAsync(resolved, warnings, log.Write, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                foreach (string warning in warnings)
                {
                    renderer.RenderWarning(warning);
                }
            }

            LogExchange(log, resolved, response);
            HttpExchange exchange = new(resolved, response);
            if (request.Name is not null)
            {
                exchanges.Add(request.Name, exchange);
            }

            return exchange;
        }
    }

    private static void WarnOfUnusedVariables(
        HttpFile file,
        IReadOnlyDictionary<string, string>? overrides,
        ResponseRenderer renderer)
    {
        if (overrides is null)
        {
            return;
        }

        HashSet<string> referenced = file.ReferencedVariableNames();
        foreach (string name in overrides.Keys.Where(name => !referenced.Contains(name)))
        {
            renderer.RenderWarning(Messages.Format(Resources.VarOptionNotUsed, name, file.Path));
        }
    }

    private static void RenderWrongTenantHint(ResponseRenderer renderer, HttpExchange exchange)
    {
        ReceivedResponse response = exchange.Response;
        if (response.StatusCode != UnauthorizedStatusCode || !exchange.Request.UsesEntraAccessToken)
        {
            return;
        }

        bool isInChallenge = response.Headers.Any(header =>
            HeaderNames.Is(header, HeaderNames.WwwAuthenticate)
            && header.Value.Contains(WrongTenantCode, StringComparison.Ordinal));
        if (isInChallenge || response.Body.Span.IndexOf(s_wrongTenantCodeUtf8) >= 0)
        {
            renderer.RenderHint(Resources.WrongTenantHint);
        }
    }

    private static void LogExchange(DiagnosticLog log, ResolvedRequest request, ReceivedResponse response)
    {
        log.Write(Messages.Format(
            Resources.VerboseExchange,
            request.Method,
            request.DisplayUrl,
            ResponseRenderer.StatusText(response),
            ResponseRenderer.Milliseconds(response)));

        // A body in a coding that is not decoded keeps its warning instead.
        IReadOnlyList<string> codings = response.ContentCodings;
        if (codings.Count > 0 && response.UnsupportedCodings.Count == 0)
        {
            log.Write(Messages.Format(Resources.VerboseDecoded, string.Join(Messages.ListSeparator, codings)));
        }
    }

    // A name wins over a title, so a title never hides a request that has that name.
    private static RequestDefinition? FindRequest(IReadOnlyList<RequestDefinition> requests, string requestName)
    {
        RequestDefinition? named = requests.FirstOrDefault(
            request => string.Equals(request.Name, requestName, StringComparison.Ordinal));
        if (named is not null)
        {
            return named;
        }

        RequestDefinition[] titled =
        [
            .. requests.Where(
                request => string.Equals(request.Title, requestName, StringComparison.OrdinalIgnoreCase)),
        ];
        if (titled.Length > 1)
        {
            throw new HttpFileException(Messages.Format(
                Resources.RequestTitleAmbiguous,
                requestName,
                string.Join(Messages.ListSeparator, titled.Select(request => request.Line))));
        }

        return titled.SingleOrDefault();
    }

    private async Task<RequestDefinition> SelectRequestAsync(
        HttpFile file,
        string? requestName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RequestDefinition> requests = file.Requests;
        if (requests.Count == 0)
        {
            throw new HttpFileException(Messages.Format(Resources.NoRequests, file.Path));
        }

        string[] names =
        [
            .. requests
                .SelectMany(request => (string?[])[request.Name, request.Title])
                .OfType<string>()
                .Distinct(StringComparer.Ordinal),
        ];
        string nameList = string.Join(Messages.ListSeparator, names);

        if (requestName is not null)
        {
            return FindRequest(requests, requestName)
                ?? throw new HttpFileException(names.Length == 0
                    ? Messages.Format(Resources.RequestNameNotFoundNoneNamed, requestName, file.Path)
                    : Messages.Format(Resources.RequestNameNotFound, requestName, file.Path, nameList));
        }

        if (requests.Count == 1)
        {
            return requests[0];
        }

        if (!console.IsInputRedirected && !console.IsOutputRedirected && picker.CanPick)
        {
            return await picker.PickAsync(requests, cancellationToken).ConfigureAwait(false)
                ?? throw new HttpFileException(Resources.NoRequestSelected);
        }

        throw new HttpFileException(names.Length == 0
            ? Messages.Format(Resources.MultipleRequestsNoneNamed, file.Path, requests.Count)
            : Messages.Format(Resources.MultipleRequestsNoName, file.Path, requests.Count, nameList));
    }

    private DynamicVariables CreateDynamicVariables(string httpFilePath, DiagnosticLog log)
    {
        // Kept as a task so that .env is read once however many expressions use it.
        Task<DotEnvFile>? dotEnv = null;

        return new DynamicVariables(
            timeProvider,
            Random.Shared,
            getEnvironmentVariable,
            cancellationToken => dotEnv ??= ReadFilesAsync(
                () => LoadDotEnvAsync(httpFilePath, log, cancellationToken)));
    }
}
