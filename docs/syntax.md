# Syntax

[Back to README](../README.md)

The tool reads the Visual Studio 2026 18.13 `.http` syntax. See
[Use .http files in Visual Studio](https://learn.microsoft.com/aspnet/core/test/http-files) and the
[18.13 release notes](https://learn.microsoft.com/visualstudio/releases/2026/release-notes-insiders).

## Request dependencies

If the selected request uses `{{x.response...}}` or `{{x.request...}}`, request `x` is sent first, on every
run, whatever its method. A non-2xx dependency stops the run with exit code 1.

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
