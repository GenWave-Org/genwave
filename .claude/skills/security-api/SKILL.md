---
name: security-api
description: >-
  API and service security review for ASP.NET Core (.NET 9) backends:
  JWT authentication and policy/role authorization, IDOR/object-level
  checks, injection (SQL via Npgsql/EF Core, command injection via
  Process.Start), SSRF, mass assignment (binding request bodies to
  entities), insecure deserialization, file upload and path traversal
  (including media libraries on network mounts), secrets and
  configuration handling, CORS, security headers and cookie flags, rate
  limiting, and multi-role systems (SuperAdmin/Admin/User). Use when
  reviewing or writing ASP.NET Core controllers, minimal APIs,
  middleware, hosted services, or auth code for vulnerabilities,
  triaging a security finding, or answering "is this exploitable / how
  do I fix it" questions about a C# backend. Ships secure-default
  config templates and a finding report format. (For the React/TS
  Admin UI, use `security-web` instead.)
---

# ASP.NET Core API Security Review

Treat every byte that crossed a network boundary as hostile until proven
otherwise, and prove it on the server. The Admin UI is an untrusted
rendering surface; so is every other HTTP client. ASP.NET Core adds
convenience (model binding, middleware, minimal APIs) that *hides* the
trust boundary — this skill's job is to make the reviewer see the
boundary the framework obscured.

## 🎯 Why: Design for Change

The goal of writing software is to be able to **change it safely**.
Secure-by-default config (authorization fallback policies, parameterized
queries, DTO binding, named CORS policies) means the next endpoint ships
without re-litigating the threat model. Every insecure default you
accept becomes a foothold the next change has to step around.

This skill is for *security review and secure implementation* of the C#
backend. For general C# correctness use `csharp-best-practices`; for
ASP.NET Core structure use `aspnetcore-patterns`; for schema-level
integrity use `postgres-dba`; for the React Admin UI use `security-web`.
This skill never assumes another layer will catch it.

## How to use this skill (review workflow)

1. **Map the attack surface first.** Every trust boundary: controller
   actions, minimal API endpoints, SignalR hubs, file uploads, outbound
   `HttpClient` calls with user-influenced URLs, configuration loaded
   from the database, anything reading paths from request data. A vuln
   you didn't enumerate is a vuln you didn't find.
2. **Triage each input against the decision guide** below; open the
   matching `references/*.md` for the exploit, the vulnerable pattern,
   the fix, and the false-positive caveat.
3. **Report findings in the standard format** (`templates/finding.md`):
   severity, location (`file:line`), a concrete exploit walkthrough, and
   the minimal fix. No exploit path stated → it is an observation, not a
   finding; say which.
4. **Recommend the secure default**, not a bespoke patch. The
   `templates/` configs (headers, CORS, checklist) are the target state.

## Severity (how to rank a finding)

| Severity | Bar |
|---|---|
| **Critical** | Unauthenticated RCE, auth bypass, secret/PII mass disclosure, SQLi on a reachable path |
| **High** | IDOR on sensitive data, privilege escalation (User→Admin), SSRF to internal network, command injection behind auth, JWT validation gap |
| **Medium** | Missing `[Authorize]` on low-sensitivity data, verbose error leakage, weak rate limiting on auth endpoints, missing security headers with real impact |
| **Low** | Defense-in-depth gaps with no direct exploit, missing hardening, informational |

Rank by *exploitability and blast radius on this codebase*, not by the
generic CVSS of the bug class. State the assumption that makes it that
severity.

## The hard rules (non-negotiable defaults)

1. **All trust decisions happen server-side, per request, per object.**
   Authentication establishes *who*; authorization re-checks *may this
   principal touch this specific resource* on every access. `[Authorize]`
   answers "is someone logged in" — it does not answer "may user 7 read
   station 12's analytics". Object-level checks are mandatory — assume
   every ID in a request was tampered (IDOR). In a multi-role system
   (SuperAdmin/Admin/User), check the role *and* the ownership scope.
   See `references/aspnetcore.md` §authz.

2. **Endpoints are deny-by-default.** Set a fallback authorization
   policy (`options.FallbackPolicy = RequireAuthenticatedUser`) so an
   endpoint someone forgot to attribute is closed, not open.
   `[AllowAnonymous]` is an explicit, reviewable decision. A new
   controller with no auth attributes is a finding until proven
   intentional.

3. **Untrusted data is validated at the boundary into a DTO — never
   bound to an entity.** Model binding will happily populate any
   settable property the client sends (`"role": "SuperAdmin"`, `"id":
   42`). Bind to a request-specific DTO containing only the fields the
   client may set, validate it, then map. `Update(entity, request)`
   where `request` is the EF entity type is mass assignment. See
   `references/aspnetcore.md` §mass-assignment.

4. **Injection is closed by structure, not by filtering.** Parameterized
   queries always: Npgsql parameters, EF Core LINQ, or
   `FromSqlInterpolated` (never `FromSqlRaw` with concatenation).
   Processes: `ProcessStartInfo` with `ArgumentList` and
   `UseShellExecute = false` — never a shell string built from input
   (relevant anywhere ffmpeg/audio tools are invoked with user-supplied
   filenames). Denylists and escaping-by-hand are findings.

5. **Outbound requests to user-influenced URLs are denied by default.**
   SSRF: allowlist host + scheme, resolve and reject private/link-local/
   metadata IPs, re-validate redirects. "We fetch the stream/webhook URL
   the admin configured" is still SSRF if a lower-privileged role can
   write that config.

6. **File paths from request data are canonicalized and jailed.** Any
   filename/path from a request (or from DB config a user can edit) is
   combined with the trusted root via `Path.GetFullPath` and verified to
   still start with that root before any read/write/delete. Upload
   filenames are regenerated server-side, never trusted. Media libraries
   on network mounts widen the blast radius: traversal escapes the
   container *and* the host. See `references/aspnetcore.md` §files.

7. **JWTs are validated strictly and carried safely.**
   `TokenValidationParameters`: validate issuer, audience, lifetime, and
   signing key — all four, explicitly. No `none` algorithm, no key from
   config defaults, clock skew bounded. Access tokens short-lived;
   refresh tokens stored server-side and revocable. Secrets for signing
   come from environment/secret store, never appsettings.json in the
   repo. See `references/jwt.md`.

8. **Secrets never live in the repo or the image.** No connection
   strings with real passwords in `appsettings.json`, no API keys in
   compose files committed, no secrets baked into Docker layers. Use
   environment variables / user-secrets / mounted secret files. Treat
   any secret that ever shipped as compromised: the fix is rotation,
   not deletion.

9. **CORS is a named allowlist, never `*` with credentials.** One named
   policy listing the exact Admin UI origins. `AllowAnyOrigin` +
   `AllowCredentials` won't even start — but reflecting the request
   origin manually re-creates the same hole. See `templates/cors.md`.

10. **Fail closed and quiet.** On auth/validation failure: deny, log
    server-side with context (correlation ID, principal, resource),
    return a generic `ProblemDetails`. No stack traces, no SQL, no
    internal hostnames, no "user not found vs wrong password" oracles.
    `UseDeveloperExceptionPage` never ships outside Development.

## Decision guide

| Symptom / what you're reviewing | Where |
|---|---|
| Controller/endpoint with no `[Authorize]`, or `[AllowAnonymous]` | `references/aspnetcore.md` §authn |
| An ID from route/query/body used to fetch or mutate a record | `references/aspnetcore.md` §authz (IDOR) |
| Role checks: who may reach admin/SuperAdmin endpoints | `references/aspnetcore.md` §authz |
| JWT issuance, validation params, refresh, key handling | `references/jwt.md` |
| `FromSqlRaw`, string-built SQL, raw Npgsql commands | `references/aspnetcore.md` §injection |
| `Process.Start` (ffmpeg, shell tools) with request-derived args | `references/aspnetcore.md` §injection |
| Request body bound to an EF entity / `TryUpdateModelAsync` on entity | `references/aspnetcore.md` §mass-assignment |
| Server fetches a URL from request/config (webhooks, stream relays) | `references/aspnetcore.md` §ssrf |
| File upload, download-by-name, path built from request data | `references/aspnetcore.md` §files |
| `BinaryFormatter`, `TypeNameHandling`, polymorphic deserialization | `references/aspnetcore.md` §deserialization |
| Login/token endpoints without throttling; enumeration | `references/aspnetcore.md` §abuse |
| CORS config | `templates/cors.md` |
| Response headers, cookie flags | `templates/security-headers.md` |
| Connection strings, API keys, signing keys in config files | `references/aspnetcore.md` §secrets |
| Middleware order (auth after endpoints, CORS after routing, etc.) | `references/aspnetcore.md` §middleware-order |
| React/TS Admin UI code | use `security-web` |

## Reference files

- `references/aspnetcore.md` — server-side rules in depth: authn
  coverage and fallback policies, authz/IDOR in a role hierarchy,
  injection (SQL/command), mass assignment via model binding, SSRF,
  file handling/path traversal, deserialization, secrets/config, rate
  limiting/abuse, middleware ordering traps, error handling.
- `references/jwt.md` — token validation parameters one by one, key
  management, lifetimes and refresh, revocation, common ASP.NET Core
  JWT misconfigurations.

## Templates (recommend these as the target state)

| File | Use |
|---|---|
| `templates/finding.md` | The report format every finding must follow |
| `templates/security-headers.md` | Headers + cookie flags as ASP.NET Core middleware |
| `templates/cors.md` | Named-policy origin allowlist |
| `templates/review-checklist.md` | The pass to run before sign-off |

## What this skill will not do

- Bless input *sanitization by denylist/regex* as an injection fix.
  Structure (parameterization, `ArgumentList`, canonicalized paths) only.
- Bless client-side-only authorization (the Admin UI hiding a button is
  UX, not a control).
- Bless `[Authorize]` alone as an object-level check.
- Bless `AllowAnyOrigin` with credentials, or manual origin reflection
  without an allowlist.
- Bless a custom crypto/JWT/password-hashing implementation where ASP.NET
  Core Identity / `Microsoft.AspNetCore.Cryptography.KeyDerivation` /
  a vetted library exists.
- Downgrade a finding to "won't fix" inside this skill — it reports
  severity and the fix; the human owns acceptance.
