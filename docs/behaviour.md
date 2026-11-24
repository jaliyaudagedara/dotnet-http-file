# Behaviour

[Back to README](../README.md)

- **Content headers** on a request without a body are left out, as Visual Studio does. `Content-Type` is
  logged under `-v`, any other gives a warning.
- **Redirects** are followed, up to 50. `Authorization`, `Proxy-Authorization`, `Cookie` and any header that
  holds a secret-provider value are not sent to another origin. Only `http` and `https` redirects are
  followed, and never from `https` to `http`.
- **TLS:** certificates are validated. For a dev certificate, run `dotnet dev-certs https --trust` or pass
  `--insecure`.
- **Cookies** are not stored between requests. Write a `Cookie` header to send one.
- **Timeout:** 100 seconds per request, redirects included.
- **Compressed responses:** gzip (also `x-gzip`) and deflate are decoded. Other codings, such as `br`, are
  left as received, with a warning. A body that is corrupt, cut short, or decodes to more than 100 MiB is an
  error (exit code 1), and nothing of the response is printed. Raw deflate (without a zlib header) has no
  checksum, so some corruption in it goes unnoticed, more often on .NET 10.
