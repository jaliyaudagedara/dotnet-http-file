# Syntax

[Back to README](../README.md)

## Supported syntax

The syntax is the one documented for Visual Studio at
https://learn.microsoft.com/aspnet/core/test/http-files:

- Request line `METHOD URL [HTTP-version]`, with `###` between requests. The method is one of `GET`, `POST`,
  `PUT`, `PATCH`, `DELETE`, `HEAD`, `OPTIONS`, `TRACE` and `CONNECT`, and a line with a URL alone is a `GET`.
  The version is `HTTP/1.1` (the default), `HTTP/2` or `HTTP/3`. `HTTP/1.0` is accepted as well.
- Headers directly after the request line, and a body after a blank line.
- `#` and `//` comment lines.
- File variables, `@Name=Value`. They are case-sensitive, must be defined before they are used, and can use
  variables defined earlier.
- `{{variable}}` in the URL, the headers and the body.
- Request variables: name a request with `# @name login`, then read from it with
  `{{login.response.body.$.token}}`. The general form is
  `{{name.(response|request).(body|headers).(*|JSONPath|XPath|HeaderName)}}`:
  - `body.*` is the whole body.
  - `body.$...` is a JSONPath (RFC 9535). A string comes back without quotes, an object or array as
    pretty-printed JSON, and several matches as a compact JSON array.
  - `body./...` is an XPath for an XML body. It returns the text value of the node it matches.
  - `headers.<Name>` is a header value. Header names are not case-sensitive.
- Dynamic variables:
  - `{{$guid}}`
  - `{{$randomInt [min max]}}`
  - `{{$timestamp [offset unit]}}`
  - `{{$datetime [format] [offset unit]}}` and `{{$localDatetime [format] [offset unit]}}`. The format is
    `rfc1123`, `iso8601` or a custom format in quotes. Offset units are `ms`, `s`, `m`, `h`, `d`, `w`, `M`
    and `y`.
  - `{{$processEnv NAME}}` reads an environment variable.
  - `{{$dotenv NAME}}` reads `NAME` from a `.env` file. The file is looked for in the folder of the nearest
    `.csproj`, searching up from the `.http` file. If there is no project, it is looked for next to the
    `.http` file.

A request body without a `Content-Type` header is sent as `text/plain; charset=utf-8`.

Environment files and secret providers are described in
[Environments and secrets](environments-and-secrets.md).

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
