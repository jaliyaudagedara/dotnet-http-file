# Known differences from Visual Studio

[Back to README](../README.md)

Compared with Visual Studio 2026 Insiders 18.13:

- **No added request headers.** Visual Studio adds `traceparent` and `Accept-Encoding: gzip, deflate` to
  each request. The tool sends only the headers written in the file, plus the ones HTTP needs, such as
  `Host` and `Content-Length`.
- **Certificates are validated.** Visual Studio accepts any server certificate. The tool does not, unless you
  pass `--insecure`.
- **XPath is accepted where Visual Studio rejects it.** Visual Studio rejects `body./token` as malformed, and
  rejects `body.//token` unless the response `Content-Type` is exactly `application/xml`. The tool accepts
  XPath expressions that start with `/`, and any XML content type (`application/xml`, `text/xml`, `+xml`,
  with or without parameters). A file that uses XPath can work in the tool and fail in Visual Studio.
- **The 18.13 additions are not supported:** GraphQL (`X-Request-Type: graphql`), Windows authentication
  (`Authorization: Negotiate` or `NTLM`), gzip and deflate decoding, readable (automatically encoded) URLs,
  `{{...}}` escaping, and the nested-variable fixes.
- **`EntraAccessToken` requests can't run in Visual Studio.** Visual Studio loads an environment file that
  uses the provider and resolves every other variable in it, so every other request in the file still runs
  there. Only requests that use an `EntraAccessToken` variable fail.
- **Dependencies are sent automatically**, as described under
  [Request dependencies](syntax.md#request-dependencies).
- **Line endings** of referenced JSON objects and arrays follow the operating system, so on Linux and macOS
  they are LF where Visual Studio on Windows produces CRLF.

The tool adds no `.http` syntax. `EntraAccessToken` in the environment file is its only addition.
