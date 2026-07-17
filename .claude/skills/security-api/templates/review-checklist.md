# API security review checklist — run before sign-off

Work the diff, not the whole repo. Every "no" needs a finding or a
written justification.

## Authentication & authorization

- [ ] Fallback authorization policy in place (deny-by-default); every
      `[AllowAnonymous]` in the diff has a stated reason.
- [ ] Every endpoint touching a specific resource scopes the query to
      the principal (no bare `FindAsync(id)` on owned data).
- [ ] Role-gated endpoints use named policies; nothing grants a role ≥
      the caller's own.
- [ ] JWT validation: issuer, audience, lifetime, signing key — all
      four validated (references/jwt.md §validation).

## Input & injection

- [ ] All request bodies bind to operation-specific DTOs, never EF
      entities.
- [ ] No string-built SQL: LINQ, `FromSqlInterpolated`, or parameters
      only.
- [ ] `Process.Start` uses `ArgumentList` + `UseShellExecute = false`;
      no shell strings with request data.
- [ ] Request-derived paths canonicalized (`Path.GetFullPath`) and
      verified inside the trusted root before use.
- [ ] Upload filenames regenerated server-side; content sniffed, size
      capped.
- [ ] No `TypeNameHandling` other than `None`; no `BinaryFormatter`.

## Outbound & infrastructure

- [ ] User/config-influenced outbound URLs: scheme+host allowlisted,
      private IP ranges rejected at connect time, redirects controlled.
- [ ] CORS: named policy, exact origins, no any-origin+credentials
      (templates/cors.md).
- [ ] Security headers + nosniff on responses
      (templates/security-headers.md).
- [ ] Auth endpoints rate-limited; login errors don't reveal which
      factor failed.

## Secrets & errors

- [ ] No real credentials in appsettings/compose/Dockerfile in the
      diff; grep `Password=`, `ApiKey`, `SigningKey`, PEM headers.
- [ ] Exceptions become generic `ProblemDetails`; EF/Npgsql details
      never reach the response.
- [ ] No tokens/credentials in log statements.
- [ ] Swagger/OpenAPI not exposed anonymously outside Development.

## Verdict

Findings in `templates/finding.md` format, severity-ordered. Critical
or High anywhere in the diff → the review verdict is FAIL.
