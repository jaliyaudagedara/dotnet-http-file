# Samples

Run these from the repository root after `dotnet tool install -g dotnet-http-file`. Without installing, use
`dnx dotnet-http-file` in place of `dotnet http-file`.

## Basics

Both files call [JSONPlaceholder](https://jsonplaceholder.typicode.com), a public fake API. No sign-in or setup
needed.

- [Basics/Named.http](Basics/Named.http) names each request with `# @name`.
- [Basics/Titled.http](Basics/Titled.http) gives each request a title on its `###` line.

Run a request by its `# @name`:

```bash
dotnet http-file samples/Basics/Named.http --name getToDos
```

Or by its `###` title (case doesn't matter):

```bash
dotnet http-file samples/Basics/Titled.http --name "create a todo"
```

`getToDo` uses an id from the `getToDos` response, so the tool runs `getToDos` first:

```bash
dotnet http-file samples/Basics/Named.http --name getToDo
```

Print only part of the response:

```bash
dotnet http-file samples/Basics/Named.http --name getToDo --show status
dotnet http-file samples/Basics/Named.http --name getToDo --show headers
```

Piped, the tool writes only the body, so another tool can process it. This example uses
[jq](https://jqlang.org), a separate JSON tool you install yourself. `-r` prints the title without quotes:

```bash
dotnet http-file samples/Basics/Named.http --name getToDos | jq -r '.[0].title'
```

Override a variable for one run with `--var`. `Limit` is 3 in the file; this run asks for 1 todo:

```bash
dotnet http-file samples/Basics/Named.http --name getToDos --var Limit=1
```

See what the tool does with `-v`. The log goes to stderr, so it combines with `--show` and with a pipe:

```bash
dotnet http-file samples/Basics/Named.http --name getToDo -v --show status
dotnet http-file samples/Basics/Named.http --name getToDos -v | jq length
```

Leave out `--name` in a terminal to pick a request from a list:

```bash
dotnet http-file samples/Basics/Titled.http
```

## Entra

[Entra/Entra.http](Entra/Entra.http) calls Microsoft Graph and Azure Resource Manager with Microsoft Entra
access tokens. The tokens come from your existing sign-in, set up in
[Entra/http-client.env.json](Entra/http-client.env.json). Sign in first (`az login`, Visual Studio or
`azd auth login`).

Your own Graph profile:

```bash
dotnet http-file samples/Entra/Entra.http --name getMe --env dev
```

The subscriptions your sign-in can see:

```bash
dotnet http-file samples/Entra/Entra.http --name listSubscriptions --env dev | jq -r '.value[].displayName'
```

`-v` shows each token's scope, tenant and expiry, and the credential that supplied them, such as
`AzureCliCredential`. It never shows a token:

```bash
dotnet http-file samples/Entra/Entra.http --name getMe --env dev -v --show status
```

`callMyApi` calls your own Entra-protected API. Its comment in [Entra/Entra.http](Entra/Entra.http) says what
to set up first.

If a request fails with `InvalidAuthenticationTokenTenant`, the token is for another tenant. Set `tenantId` on
the variable in `http-client.env.json`, or see
[Environments and secrets](../docs/environments-and-secrets.md).
