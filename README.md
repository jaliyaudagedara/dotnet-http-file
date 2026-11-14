# dotnet-http-file

Run the same `.http` files that Visual Studio 2026 runs from the command line (PowerShell or bash), on Windows, Linux and macOS.
It gets Microsoft Entra access tokens from the sign-in the machine already has, so no token is pasted into a file, and it works for a person at a terminal (colour, pretty-printed bodies, a request picker) and for scripts (raw body on stdout, meaningful exit codes).

## Install

The tool needs the .NET 11 runtime. Installing it needs the .NET 11 SDK, because `dotnet tool install` and
`dnx` are SDK commands.

Install it as a global tool:

```
dotnet tool install -g dotnet-http-file
```

Once installed, the command is `dotnet http-file` or `dotnet-http-file`. Both run the same program.

Or run it without installing it. `dnx` downloads the tool to the NuGet cache and runs it from there:

```
dnx dotnet-http-file MyApi.http --name getToDos
```

### From source

Pack it from a clone of [the source](https://github.com/jaliyaudagedara/dotnet-http-file) and install from the local folder:

```
dotnet pack src/dotnet-http-file -c Release -o artifacts
dotnet tool install -g dotnet-http-file --add-source artifacts --version 0.1.0
```

Or run it from the clone without installing anything:

```
dotnet run --project src/dotnet-http-file -- MyApi.http --name getToDos
```

## Quick start

To try it straight away, `samples/Basics/Basics.http` calls the public JSONPlaceholder API and needs no
environment file:

```
dotnet run --project src/dotnet-http-file -- samples/Basics/Basics.http --name getToDos
```

`samples/Entra/` shows `EntraAccessToken`: it calls Microsoft Graph and Azure Resource Manager with a token
from your existing sign-in (for example `az login`), with no app registration needed:

```
dotnet run --project src/dotnet-http-file -- samples/Entra/Entra.http --name getMe --env dev
```

## Everyday use

Run a request by name. In a terminal this prints the full view: request, status, headers and formatted body,
with secrets masked.

```
dotnet http-file MyApi.http --name getToDos
```

Leave out `--name`:

```
dotnet http-file MyApi.http
```

- If the file has one request, that request runs.
- If stdin and stdout are both a terminal, an arrow-key picker lists every request.
- Otherwise the tool prints the named requests to stderr and exits with code 2. A request without a name
  can't be picked from a script, so add `# @name <name>` above it.

Take variables from an environment in `http-client.env.json`. Without `--env`, only the `$shared` section of
the environment file and the variables in the `.http` file apply.

```
dotnet http-file MyApi.http --name getToDos --env dev
```

Print the status line only, the status line and the response headers, or the response bytes as received:

```
dotnet http-file MyApi.http --name getToDos --show status
dotnet http-file MyApi.http --name getToDos --show headers
dotnet http-file MyApi.http --name getToDos --show body
```

Use it in a script. The body goes to stdout, the status line to stderr:

```
dotnet http-file MyApi.http --name getToDos --env dev | jq '.[0].title'
```

## Options

```
dotnet http-file <file.http> [--name <name>] [--env <env>] [--verbose | --show <full|body|headers|status>] [--insecure]
```

| Option | Short | Meaning |
|---|---|---|
| `<file>` | | The `.http` or `.rest` file that holds the request. |
| `--name <name>` | `-n` | The request to run, as named by `# @name <name>` or `// @name <name>` above it. |
| `--env <env>` | `-e` | The environment in `http-client.env.json` to take variables from. |
| `--verbose` | `-v` | Same as `--show full`. |
| `--show full` | | Print the full view: request, status, headers and formatted body, with secrets masked. |
| `--show headers` | | Print the status line and the response headers, masked. |
| `--show status` | | Print the status line only. |
| `--show body` | | Print the response bytes as received: not masked, control characters not neutralised. |
| `--insecure` | `-k` | Skip server certificate validation for this run. |
| `--version` | | Print the version. |
| `--help` | `-h`, `-?` | Print help. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | A response arrived with a 2xx status. |
| 1 | A response arrived with any other status (the body is still printed), or a dependency returned a non-2xx status and the selected request was not sent. |
| 2 | Nothing was sent: bad arguments, a file that is missing or can't be read, a parse error, an unknown request name, an undefined variable, a provider failure, a dependency cycle, no request picked in a script, or a network failure (DNS, connection refused, certificate not trusted, timeout). |
| 130 | Cancelled with Ctrl+C. |

Redirects are followed, so the status is that of the final response. The timeout is 100 seconds, for the
request together with the redirects it follows.

## Learn more

- [Environments and secrets](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/environments-and-secrets.md): environment files, secret providers and Entra access tokens.
- [Output](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/output.md): the `--show` modes in detail, and what is masked and what is not.
- [Syntax](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/syntax.md): the supported `.http` syntax, request dependencies, and a full example.
- [Behaviour](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/behaviour.md): redirects, TLS, cookies and other behaviour to know about.
- [Differences from Visual Studio](https://github.com/jaliyaudagedara/dotnet-http-file/blob/main/docs/differences-from-visual-studio.md): where the tool and Visual Studio differ.

## License

MIT
