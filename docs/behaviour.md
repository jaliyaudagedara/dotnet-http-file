# Behaviour

[Back to README](../README.md)

## Redirects

A response with status 300, 301, 302, 303, 307 or 308 and a `Location` header is followed, up to 50 times.
After that the redirect itself is the response. Each redirect that is followed prints one line to stderr, with
secrets masked:

```
Redirected to https://todo.contoso.com/v2/todos (302)
```

- **Method and body.** A `POST` becomes a `GET` without a body on 300, 301 and 302. On 303 every method except
  `GET` and `HEAD` becomes a `GET` without a body. 307 and 308 send the same method and body again.
- **Same origin.** When the scheme, host and port stay the same, every header is sent again, `Authorization`
  included.
- **Another origin.** When the scheme, the host or the port changes, these headers are not sent to the new
  address or to any redirect after it: `Authorization`, `Proxy-Authorization`, `Cookie`, and every header
  whose value holds a value that came from a secret provider. Other headers are sent as written. The body of
  a 307 or 308 is sent again whatever it holds.
- A redirect from `https` to `http` is not followed. The redirect itself is the response.
- The full view shows the request as it is written in the file: the first URL and all of its headers. The
  response is the final one.

## TLS

Server certificates are always validated. For a local HTTPS endpoint with a development certificate, pick
one of these:

- Trust the certificate: `dotnet dev-certs https --trust`.
- Use the `http://` address of the app.
- Pass `--insecure`, which skips certificate validation for that run only.

A bearer token is never sent to a server whose certificate was not validated unless you pass `--insecure`.

## Other behaviour to know about

- **Cookies** are not carried between requests. A `Set-Cookie` from a response is never stored, so it is not
  sent with the next request or with a redirect that follows it. To send a cookie, write a `Cookie` header in
  the file. A written `Cookie` header goes along with a redirect to the same origin and is dropped on a
  redirect to another origin, as described under Redirects.
- **Header line breaks**: a header whose value contains a line break is refused and the request is not sent.
  This includes a JSON object or array that is referenced into a header, because it is pretty-printed over
  several lines.
- **Line endings**: a JSON object or array that is referenced into a request is pretty-printed with the
  newline of the operating system. That is LF on Linux and macOS, and CRLF on Windows, as in Visual Studio.
- **XPath**: XML default namespaces and namespace prefixes are not supported. An XPath has to match exactly
  one node.
- **Duplicate JSON names**: a response body that has a JSON object with a repeated property name can't be
  read with a JSONPath, so a request that references it is not sent.
- **Response files** are not supported, so an argument that starts with `@` is passed through as written.
