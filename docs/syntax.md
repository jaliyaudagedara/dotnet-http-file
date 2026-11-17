# Syntax

[Back to README](../README.md)

The tool reads the Visual Studio 2026 18.13 `.http` syntax. See
[Use .http files in Visual Studio](https://learn.microsoft.com/aspnet/core/test/http-files) and the
[18.13 release notes](https://learn.microsoft.com/visualstudio/releases/2026/release-notes-insiders).

## Request names

`--name` matches a `# @name` first (case-sensitive), else a `### <title>` (case-insensitive), so a request can be
picked with or without a `# @name`. If two requests share the matched title, the run stops with an error.

## Request dependencies

If the selected request uses `{{x.response...}}` or `{{x.request...}}`, request `x` is sent first, on every
run, whatever its method. A non-2xx dependency stops the run with exit code 1. A file variable overridden
with `--var` is not followed, so a request that only its definition refers to is not sent.

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

Every backslash right before `{{` is that escape, also one that belongs to the text: `CONTOSO\{{user}}` sends
`CONTOSO{{user}}`. To send the backslash and the value, put the text with its backslash in a file variable:

```http
@Domain=CONTOSO\
@user=jaliya

GET {{HostAddress}}/whoami
X-Account: {{Domain}}{{user}}
```

This sends `X-Account: CONTOSO\jaliya`.
