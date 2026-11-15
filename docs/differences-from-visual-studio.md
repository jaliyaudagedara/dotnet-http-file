# Differences from Visual Studio

[Back to README](../README.md)

Compared with Visual Studio 2026 Insiders 18.13:

- **No added headers.** VS adds `traceparent` and `Accept-Encoding: gzip, deflate`. The tool sends only what
  the file writes.
- **Certificates are validated.** VS accepts any certificate. Use `--insecure` for the same.
- **Dependencies are sent automatically.** See [Request dependencies](syntax.md#request-dependencies).
- **No Windows authentication.** A bare `Authorization: Negotiate` or `NTLM` header is sent as written.
- **`EntraAccessToken`** is the tool's own provider. VS can't resolve it.
- **XPath** starting with `/` is accepted, with any XML content type. VS rejects it.
- **Not checked against VS:** GraphQL queries with blank lines inside, without variables or without
  `Content-Type`, and wildcard JSONPath with zero or one match.
