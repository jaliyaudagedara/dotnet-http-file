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
internal sealed class HttpFileRunner(
    ConsoleStreams console,
    IRequestPicker picker,
    SecretProviderRegistry providers,
    Func<bool, HttpMessageHandler> createHandler,
    TimeProvider timeProvider,
    Func<string, string?> getEnvironmentVariable)
{
    private const string DotEnvFileName = ".env";

    private static readonly CompositeFormat s_dependencyFailedFormat =
        CompositeFormat.Parse(Resources.DependencyFailed);

    private static readonly CompositeFormat s_fileNotFoundFormat = CompositeFormat.Parse(Resources.FileNotFound);

    private static readonly CompositeFormat s_multipleRequestsNoNameFormat =
        CompositeFormat.Parse(Resources.MultipleRequestsNoName);

    private static readonly CompositeFormat s_multipleRequestsNoneNamedFormat =
        CompositeFormat.Parse(Resources.MultipleRequestsNoneNamed);

    private static readonly CompositeFormat s_noRequestsFormat = CompositeFormat.Parse(Resources.NoRequests);

    private static readonly CompositeFormat s_requestNameNotFoundFormat =
        CompositeFormat.Parse(Resources.RequestNameNotFound);

    private static readonly CompositeFormat s_requestNameNotFoundNoneNamedFormat =
        CompositeFormat.Parse(Resources.RequestNameNotFoundNoneNamed);

    private static readonly CompositeFormat s_unexpectedErrorFormat =
        CompositeFormat.Parse(Resources.UnexpectedError);

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

        try
        {
            return await RunCoreAsync(options, secretMask, renderer, cancellationToken).ConfigureAwait(false);
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
            renderer.RenderError(Messages.Format(s_unexpectedErrorFormat, exception.GetType().Name, Detail(exception)));

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
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(httpFilePath);
        string httpFileDirectory = Path.GetDirectoryName(fullPath) ?? fullPath;
        string directory = ProjectLocator.FindProjectDirectory(httpFileDirectory) ?? httpFileDirectory;

        return await DotEnvFile.LoadAsync(Path.Combine(directory, DotEnvFileName), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<int> RunCoreAsync(
        RunOptions options,
        SecretMask secretMask,
        ResponseRenderer renderer,
        CancellationToken cancellationToken)
    {
        string filePath = options.FilePath;
        if (!File.Exists(filePath))
        {
            throw new HttpFileException(Messages.Format(s_fileNotFoundFormat, filePath));
        }

        HttpFile file = await ReadFilesAsync(() => HttpFileParser.LoadAsync(filePath, cancellationToken))
            .ConfigureAwait(false);
        EnvironmentSet environment = await ReadFilesAsync(() => EnvironmentLoader.LoadAsync(
            EnvironmentFileLocator.Find(filePath),
            options.EnvironmentName,
            filePath,
            cancellationToken)).ConfigureAwait(false);

        RequestDefinition target = SelectRequest(file, options.RequestName);
        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, target);

        ExchangeStore exchanges = new();
        VariableResolver resolver = new(
            file,
            environment,
            providers,
            CreateDynamicVariables(filePath),
            exchanges,
            secretMask);

        using HttpClient httpClient = ToolHttpClient.Create(createHandler(options.Insecure));
        RequestSender sender = new(httpClient, secretMask, renderer.RenderRedirect);

        foreach (RequestDefinition dependency in plan.Take(plan.Count - 1))
        {
            HttpExchange exchange = await SendAsync(dependency).ConfigureAwait(false);

            renderer.RenderDependency(dependency, exchange);
            if (!exchange.Response.IsSuccess)
            {
                renderer.RenderError(Messages.Format(
                    s_dependencyFailedFormat,
                    dependency.Name,
                    ResponseRenderer.StatusText(exchange.Response),
                    target.Name ?? $"{target.Method} {target.Url.Raw}"));

                return ExitCodes.ErrorStatus;
            }
        }

        HttpExchange targetExchange = await SendAsync(target).ConfigureAwait(false);
        await renderer.RenderAsync(targetExchange, cancellationToken).ConfigureAwait(false);

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
                response = await sender.SendAsync(resolved, warnings, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                foreach (string warning in warnings)
                {
                    renderer.RenderWarning(warning);
                }
            }

            HttpExchange exchange = new(resolved, response);
            if (request.Name is not null)
            {
                exchanges.Add(request.Name, exchange);
            }

            return exchange;
        }
    }

    private RequestDefinition SelectRequest(HttpFile file, string? requestName)
    {
        IReadOnlyList<RequestDefinition> requests = file.Requests;
        if (requests.Count == 0)
        {
            throw new HttpFileException(Messages.Format(s_noRequestsFormat, file.Path));
        }

        string[] names = [.. requests.Select(request => request.Name).OfType<string>()];
        string nameList = string.Join(Messages.ListSeparator, names);

        if (requestName is not null)
        {
            return requests.FirstOrDefault(
                    request => string.Equals(request.Name, requestName, StringComparison.Ordinal))
                ?? throw new HttpFileException(names.Length == 0
                    ? Messages.Format(s_requestNameNotFoundNoneNamedFormat, requestName, file.Path)
                    : Messages.Format(s_requestNameNotFoundFormat, requestName, file.Path, nameList));
        }

        if (requests.Count == 1)
        {
            return requests[0];
        }

        if (!console.IsInputRedirected && !console.IsOutputRedirected)
        {
            return picker.Pick(requests) ?? throw new HttpFileException(Resources.NoRequestSelected);
        }

        throw new HttpFileException(names.Length == 0
            ? Messages.Format(s_multipleRequestsNoneNamedFormat, file.Path, requests.Count)
            : Messages.Format(s_multipleRequestsNoNameFormat, file.Path, requests.Count, nameList));
    }

    private DynamicVariables CreateDynamicVariables(string httpFilePath)
    {
        // Kept as a task so that .env is read once however many expressions use it.
        Task<DotEnvFile>? dotEnv = null;

        return new DynamicVariables(
            timeProvider,
            Random.Shared,
            getEnvironmentVariable,
            cancellationToken => dotEnv ??= ReadFilesAsync(() => LoadDotEnvAsync(httpFilePath, cancellationToken)));
    }
}
