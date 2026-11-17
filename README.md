# dotnet-http-file

Run Visual Studio 2026 `.http` files from the command line, on Windows, Linux and macOS. Microsoft Entra
access tokens come from your existing sign-in, so no token is pasted into a file.

## Install

Needs the .NET 11 SDK.

```bash
dotnet tool install -g dotnet-http-file
```

Or run it without installing: `dnx dotnet-http-file MyApi.http --name getToDos`.

## Use

```bash
dotnet http-file MyApi.http --name getToDos --env dev
dotnet http-file MyApi.http --name getToDos --show status
dotnet http-file MyApi.http --name getToDos | jq '.[0].title'
```

In a terminal it prints the request, with secrets masked, then the response status, headers and formatted body
as the server sent them. When piped, it writes the raw body to stdout. Without `--name`, a terminal gets a
request picker.

Try [`samples/Basics/Named.http`](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/samples/Basics/Named.http)
and [`samples/Basics/Titled.http`](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/samples/Basics/Titled.http)
(no setup), or [`samples/Entra/Entra.http`](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/samples/Entra/Entra.http)
(needs `az login`).

## Options

| Option | Meaning |
|---|---|
| `-n`, `--name <name>` | The request to run, from `# @name <name>` or a `### <title>`. |
| `-e`, `--env <env>` | The environment in `http-client.env.json`. |
| `-v`, `--verbose` | Log what the tool does (files read, requests, tokens acquired) to stderr, never a token or secret-provider value. Changed in 0.2: it no longer means `--show full`. |
| `--show <full\|body\|headers\|status>` | What to print. |
| `-k`, `--insecure` | Skip server certificate validation. |
| `--var <name=value>` | Set a variable for this run, over the file and the environment. Can be repeated; the last one wins. Names are case-sensitive, and a name no `{{name}}` in the file uses gets a warning. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | 2xx response. |
| 1 | A non-2xx response from the selected request or from a request it depends on, or a response body that is not valid in its coding or decodes to more than 100 MiB. |
| 2 | The selected request got no response: bad arguments, parse error, undefined variable, provider failure, network failure or timeout. Dependencies may already have been sent. Also any unexpected error in the tool. |
| 130 | Cancelled with Ctrl+C. |

## Docs

- [Environments and secrets](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/environments-and-secrets.md)
- [Output](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/output.md)
- [Syntax](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/syntax.md)
- [Behaviour](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/behaviour.md)
- [Differences from Visual Studio](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/differences-from-visual-studio.md)

## License

MIT
