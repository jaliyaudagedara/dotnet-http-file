# Differences from Visual Studio

[Back to README](../README.md)

Compared with Visual Studio 2026 Insiders 18.13:

- **No added headers.** VS adds headers such as `traceparent` and `Accept-Encoding: gzip, deflate`. The tool
  sends only what the file writes.
- **Certificates are validated.** VS accepts any certificate. Use `--insecure` for the same.
- **Dependencies are sent automatically.** See [Request dependencies](syntax.md#request-dependencies).
- **No Windows authentication.** A bare `Authorization: Negotiate` or `NTLM` header is sent as written.
- **`EntraAccessToken`** is the tool's own provider. VS can't resolve it.
- **XPath** works in any form and with any `Content-Type`. VS rejects `/root/token`, and accepts `//token`
  only when the `Content-Type` is exactly `application/xml`.
- **GraphQL:**
  - The query can hold blank lines. VS splits the body at the first blank line and fails with HTTP0029 when
    the rest is not JSON.
  - The variables are the JSON object at the end of the body. Everything before it is the query. A JSON array
    there is part of the query; VS sends it as the variables.
  - If no JSON object ends the body, a block after a blank line that starts like one (`{"` or `{}`) is an
    error (exit code 2), since it is most likely broken variables.
  - Without a `Content-Type`, the request is sent as `application/json`. VS sends `text/plain; charset=utf-8`.
