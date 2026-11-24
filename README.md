# dotnet-http-file

Run `.http` files from the command line, on Windows, Linux and macOS. The tool reads the
[Visual Studio 2026 `.http` syntax](https://learn.microsoft.com/en-us/aspnet/core/test/http-files).
Microsoft Entra access tokens come from your existing sign-in, so no token is pasted into a file.

## Install

Needs the .NET 10 SDK or later. It installs as a [.NET tool](https://learn.microsoft.com/dotnet/core/tools/global-tools).

```bash
dotnet tool install -g dotnet-http-file
dotnet tool update -g dotnet-http-file
dotnet tool uninstall -g dotnet-http-file
```

Or run it without installing: `dnx dotnet-http-file MyApi.http --name getToDos`.

## Use

```bash
# Run the getToDos request with the dev environment from http-client.env.json
dotnet http-file MyApi.http --name getToDos --env dev

# Print only the response status line
dotnet http-file MyApi.http --name getToDos --show status

# Pipe the response body to jq
dotnet http-file MyApi.http --name getToDos | jq '.[0].title'
```

In a terminal it prints the request, with secrets masked, then the response status, headers and formatted body.
When piped, it writes only the response body. Without `--name`, a file with one request runs it, and in a
terminal a file with more shows a list to pick from.

Try the [samples](https://github.com/jaliyaudagedara/dotnet-http-file/tree/main/samples). The Basics need no
setup; the Entra sample needs `az login`.

## Options

| Option | Meaning |
|---|---|
| `-n`, `--name <name>` | The request to run, from `# @name <name>` or a `### <title>`. |
| `--list` | Print the name, else the title, of each request `--name` can select, one per line, and send nothing. Warn about the rest. Can't be used with `--name`. |
| `-e`, `--env <env>` | The environment in `http-client.env.json`. |
| `-v`, `--verbose` | Log what the tool does to stderr: files read, requests, tokens acquired. Never logs a token or a secret-provider value. |
| `--show <full\|body\|headers\|status>` | What to print. |
| `-k`, `--insecure` | Skip server certificate validation. |
| `--var <name=value>` | Set a variable for this run, over the file and the environment. Can be repeated. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | 2xx response, or `--list` printed the requests. |
| 1 | The server answered with a non-2xx status, for the request or a request it depends on, or the body could not be decoded. |
| 2 | The request got no response: bad arguments, a parse error, an undefined variable, a secret provider failure, a network error, a timeout or an unexpected error. Dependencies may already have been sent. |
| 130 | Cancelled with Ctrl+C. |

## Docs

- [Environments and secrets](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/environments-and-secrets.md)
- [Output](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/output.md)
- [Syntax](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/syntax.md)
- [Behaviour](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/behaviour.md)
- [Differences from Visual Studio](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/differences-from-visual-studio.md)

## License

MIT
