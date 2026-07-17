# Security headers & cookie flags — ASP.NET Core

Target state for API responses. Add once as middleware in `Program.cs`
(or at the reverse proxy — either is fine, but it must exist exactly
once; duplicated headers confuse browsers).

## Headers middleware

```csharp
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";                 // API responses never need framing
    headers["Referrer-Policy"] = "no-referrer";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    // For a pure JSON API a restrictive CSP still helps if a response is
    // ever rendered (error pages, swagger):
    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    await next();
});
```

## HSTS / HTTPS

```csharp
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();             // sets Strict-Transport-Security
}
app.UseHttpsRedirection();     // or terminate TLS at the proxy and use UseForwardedHeaders
```

Behind nginx/traefik in Docker: terminate TLS at the proxy, set HSTS
there, and configure `UseForwardedHeaders` with the proxy's network as
a known proxy so `Request.IsHttps` is true.

## Cookies (only if cookies carry auth — bearer-header APIs skip this)

```csharp
new CookieOptions
{
    HttpOnly = true,
    Secure = true,
    SameSite = SameSiteMode.Lax,   // None requires a stated cross-site reason + CSRF tokens
    MaxAge = TimeSpan.FromMinutes(15),
    Path = "/api",
}
```

## Swagger / OpenAPI

Never expose Swagger UI anonymously in production. Either gate it
behind the Admin policy or emit it only in Development:

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
```

## Checklist

- [ ] `X-Content-Type-Options: nosniff` on every response (critical for
      served/uploaded media).
- [ ] `X-Frame-Options: DENY` or CSP `frame-ancestors`.
- [ ] HSTS on HTTPS (app or proxy, once).
- [ ] No `Server`/version banners (`AddServerHeader = false` on Kestrel).
- [ ] Error responses are `ProblemDetails`, no stack traces.
- [ ] Auth cookies (if any): HttpOnly + Secure + SameSite + bounded age.
