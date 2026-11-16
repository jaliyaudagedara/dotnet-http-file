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
- **GraphQL queries can hold blank lines.** VS splits the body at the first blank line and fails with
  HTTP0029 when the rest is not JSON. The tool takes as variables the text after a blank line that is a JSON
  object, trying the last blank line first and then each one before it. When there is none, the whole body is
  the query (for example an operation, a blank line, then a fragment or an anonymous query such as
  `{ hero { name } }`). A last block that starts as a JSON object does (`{` followed by `"` or `}`) is an
  error (exit code 2): it is read as variables with a mistake in them.
- **GraphQL variables are a JSON object.** A last block that is a JSON array is part of the query. VS sends it
  as the variables.
- **GraphQL without `Content-Type`** is sent as `application/json`. VS sends `text/plain; charset=utf-8`.
- **Not checked against VS:** wildcard JSONPath with zero or one match.
