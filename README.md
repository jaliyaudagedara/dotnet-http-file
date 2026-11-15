# dotnet-http-file

Run Visual Studio 2026 `.http` files from the command line, on Windows, Linux and macOS. Microsoft Entra
access tokens come from your existing sign-in, so no token is pasted into a file.

## Install

Needs the .NET 11 SDK.

```
dotnet tool install -g dotnet-http-file
```

Or run it without installing: `dnx dotnet-http-file MyApi.http --name getToDos`.

## Use

```
dotnet http-file MyApi.http --name getToDos --env dev
dotnet http-file MyApi.http --name getToDos --show status
dotnet http-file MyApi.http --name getToDos | jq '.[0].title'
```

In a terminal it prints the request, status, headers and formatted body, with secrets masked. When piped, it
writes the raw body to stdout. Without `--name`, a terminal gets a request picker.

Try `samples/Basics/Basics.http` (no setup) or `samples/Entra/Entra.http` (needs `az login`).

## Options

| Option | Meaning |
|---|---|
| `-n`, `--name <name>` | The request to run, from `# @name <name>`. |
| `-e`, `--env <env>` | The environment in `http-client.env.json`. |
| `--show <full\|body\|headers\|status>` | What to print. `-v` is `--show full`. |
| `-k`, `--insecure` | Skip server certificate validation. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | 2xx response. |
| 1 | Non-2xx response, a dependency failed, or the body could not be decoded. |
| 2 | Nothing was sent: bad arguments, parse error, undefined variable, provider or network failure. |
| 130 | Cancelled with Ctrl+C. |

## Docs

- [Environments and secrets](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/environments-and-secrets.md)
- [Output](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/output.md)
- [Syntax](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/syntax.md)
- [Behaviour](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/behaviour.md)
- [Differences from Visual Studio](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/differences-from-visual-studio.md)

## License

MIT
