# Syntax

[Back to README](../README.md)

The tool reads the Visual Studio 2026 Insiders 18.13 `.http` syntax. See
[Use .http files in Visual Studio](https://learn.microsoft.com/en-us/aspnet/core/test/http-files) and the
[18.13 release notes](https://learn.microsoft.com/visualstudio/releases/2026/release-notes-insiders).

## Request names

`--name` picks the request with that `# @name` (case-sensitive). If none has it, it picks the request whose
`### <title>` matches, ignoring case. Two requests with that title are an error.

`--list` prints the name, else the title, of each request that `--name` can select. It warns about the rest:
a request with no name or title, a title another request also has, or a title that is another request's name.

## Request dependencies

If the selected request uses `{{x.response...}}` or `{{x.request...}}`, request `x` is sent first, on every
run, whatever its method. If `x` fails (not 2xx), the run stops with exit code 1. When `--var` overrides a
file variable, a request named only in that variable's definition is not sent.

```http
# @name createToDo
POST {{HostAddress}}/todos
Content-Type: application/json

{ "title": "Write blog post" }

###

# @name getToDo
GET {{HostAddress}}/todos/{{createToDo.response.body.$.id}}
```

Running `getToDo` sends `createToDo` first, so each run creates a record.

## Escaping `{{`

`\{{` sends `{{` as text: `\{{id}}` is sent as `{{id}}` and is not looked up.

This holds even when the backslash is part of your text: `CONTOSO\{{user}}` sends `CONTOSO{{user}}`. To send
a backslash before a value, end a file variable with it:

```http
@Domain=CONTOSO\
@user=jaliya

GET {{HostAddress}}/whoami
X-Account: {{Domain}}{{user}}
```

This sends `X-Account: CONTOSO\jaliya`.
