# Known differences from Visual Studio

[Back to README](../README.md)

Compared with Visual Studio 2026 Insiders 18.13:

- **No added request headers.** Visual Studio adds `traceparent` and `Accept-Encoding: gzip, deflate` to
  each request. The tool sends only the headers written in the file, plus the ones HTTP needs, such as
  `Host` and `Content-Length`. It decodes gzip and deflate responses but doesn't ask for them: write
  `Accept-Encoding: gzip, deflate` in a request to get what Visual Studio sends.
- **Certificates are validated.** Visual Studio accepts any server certificate. The tool does not, unless you
  pass `--insecure`.
- **XPath is accepted where Visual Studio rejects it.** Visual Studio rejects `body./token` as malformed, and
  rejects `body.//token` unless the response `Content-Type` is exactly `application/xml`. The tool accepts
  XPath expressions that start with `/`, and any XML content type (`application/xml`, `text/xml`, `+xml`,
  with or without parameters). A file that uses XPath can work in the tool and fail in Visual Studio.
- **Windows authentication is not supported.** Visual Studio signs in with the Windows identity when a
  request has a bare `Authorization: Negotiate` or `Authorization: NTLM` header. The tool sends the header
  as written.
- **GraphQL details not checked against Visual Studio.** The tool builds the request from the Visual Studio
  release notes. These cases were not compared with what Visual Studio sends:
  - A query with blank lines inside it. The tool splits at the first blank line, so the rest is read as
    variables.
  - A request without variables. The tool leaves `variables` out of the JSON it sends.
  - A request without `Content-Type`. The tool sends `application/json`.
  - A comment or comma between `query` and the operation name. The tool then sends no `operationName`.
- **Wildcard JSONPath with zero or one match** (`$.items[*].id`) gives `[]` or `[1]`, following the
  Visual Studio release notes. This was not checked against Visual Studio. Filters keep giving the bare
  value for one match.
- **`EntraAccessToken` requests can't run in Visual Studio.** Visual Studio loads an environment file that
  uses the provider and resolves every other variable in it, so every other request in the file still runs
  there. Only requests that use an `EntraAccessToken` variable fail.
- **Dependencies are sent automatically**, as described under
  [Request dependencies](syntax.md#request-dependencies).
- **Line endings** of referenced JSON objects and arrays follow the operating system, so on Linux and macOS
  they are LF where Visual Studio on Windows produces CRLF.

The tool adds no `.http` syntax. `EntraAccessToken` in the environment file is its only addition.
