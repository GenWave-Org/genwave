# CORS — named allowlist policy (ASP.NET Core)

The target state: one named policy, exact origins from configuration,
credentials only if the Admin UI actually uses cookies.

```csharp
const string AdminUiCors = "AdminUi";

builder.Services.AddCors(options =>
{
    options.AddPolicy(AdminUiCors, policy =>
    {
        policy.WithOrigins(corsOptions.AllowedOrigins) // e.g. ["https://admin.genwave.example"]
              .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
              .WithHeaders("Authorization", "Content-Type")
              .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
        // .AllowCredentials() ONLY if auth rides in a cookie. Bearer-header
        // APIs don't need it — leaving it off removes the CSRF surface.
    });
});

// pipeline: after UseRouting, before UseAuthentication
app.UseCors(AdminUiCors);
```

Origins come from configuration (env per environment), not hardcoded —
dev (`http://localhost:5173`) and prod differ.

## Never

- `AllowAnyOrigin()` together with `AllowCredentials()` — the framework
  throws, but don't work around it by reflecting the request `Origin`
  header manually; that's the same hole hand-rolled.
- `SetIsOriginAllowed(_ => true)` — same hole, quieter.
- Wildcard subdomain matching unless every subdomain is actually
  trusted infrastructure.
- "Temporary" `AllowAnyOrigin` for local dev that ships — keep dev
  origins in dev config instead.

## Review notes

- CORS protects browsers, not the API: it is **not** an authn/authz
  control. curl ignores it entirely. Every endpoint still needs its own
  auth (SKILL.md rules 1–2).
- The public audio stream endpoint may legitimately want
  `AllowAnyOrigin` (no credentials, public content) — give it its *own*
  named policy on that route group rather than loosening the admin one.
