# Syntax

[Back to README](../README.md)

## Syntax

The tool reads the `.http` syntax of Visual Studio 2026 18.13, so Microsoft's documentation is the
reference:

- [Use .http files in Visual Studio](https://learn.microsoft.com/aspnet/core/test/http-files)
- [Visual Studio Insiders release notes](https://learn.microsoft.com/visualstudio/releases/2026/release-notes-insiders),
  for the 18.13 additions: escaping, readable URLs, nested variables, JSON array chaining, compressed
  responses and GraphQL.

Where the tool behaves differently, it is listed in
[Known differences from Visual Studio](differences-from-visual-studio.md). Environment files and secret
providers are described in [Environments and secrets](environments-and-secrets.md).

## Request dependencies

In Visual Studio you send a named request by hand before another request can read its response. The tool has
no session to remember responses in, so it sends dependencies itself:

- If the selected request uses `{{x.response...}}` or `{{x.request...}}`, request `x` is sent first. This
  applies to the dependencies of `x` too. Each request is sent at most once per run.
- **Dependencies are sent on every run, whatever their method.** A dependency that is a `POST`, `PUT` or
  `DELETE` is sent again each time you run a request that depends on it. If `createToDo` creates a record,
  every run of `getToDo` creates another one.
- If a dependency returns a non-2xx status, the tool stops, reports it, and exits with code 1.
- If a referenced value can't be read (the path matches nothing, or the body is not JSON or XML), the
  request is not sent and the tool exits with code 2.
- A cycle between requests is an error, with exit code 2.

## Example

`http-client.env.json`, next to the `.http` file or in any folder above it:

```json
{
  "$shared": {
    "ApiVersion": "v1"
  },
  "dev": {
    "HostAddress": "https://localhost:7128",
    "ApiToken": {
      "provider": "EntraAccessToken",
      "scope": "api://todo-api-dev/.default"
    }
  },
  "prod": {
    "HostAddress": "https://todo.contoso.com",
    "ApiToken": {
      "provider": "EntraAccessToken",
      "scope": "api://todo-api/.default",
      "tenantId": "11111111-2222-3333-4444-555555555555"
    }
  }
}
```

`ToDo.http`:

```http
@ToDosUrl={{HostAddress}}/{{ApiVersion}}/todos

# @name getToDos
GET {{ToDosUrl}}
Authorization: Bearer {{ApiToken}}
Accept: application/json

###

# @name createToDo
POST {{ToDosUrl}}
Authorization: Bearer {{ApiToken}}
Content-Type: application/json

{
  "title": "Write blog post",
  "requestId": "{{$guid}}"
}

###

# @name getToDo
GET {{ToDosUrl}}/{{createToDo.response.body.$.id}}
Authorization: Bearer {{ApiToken}}
```

Run a request in a terminal:

```
> dotnet http-file ToDo.http --name getToDos --env dev
GET https://localhost:7128/v1/todos
Authorization: Bearer ***
Accept: application/json

HTTP/1.1 200 OK   142 ms · 312 B
Content-Type: application/json; charset=utf-8

[
  {
    "id": 1,
    "title": "Write blog post",
    "done": true
  }
]
```

Use it in a script. The body goes to stdout, the status line to stderr:

```
dotnet http-file ToDo.http --name getToDos --env dev | jq '.[0].title'
```

`getToDo` reads a value from the response of `createToDo`, so `createToDo` is sent first:

```
> dotnet http-file ToDo.http --name getToDo --env dev
→ createToDo  POST https://localhost:7128/v1/todos  201 Created  88 ms
GET https://localhost:7128/v1/todos/7
Authorization: Bearer ***
...
```
