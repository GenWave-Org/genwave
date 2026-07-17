# JWT in ASP.NET Core — validation, keys, lifetimes

JWT bugs are binary: a single relaxed validation flag usually means
full authentication bypass. Review these settings line by line — never
"it uses the standard library so it's fine".

---

## §validation — TokenValidationParameters, all four checks

The bearer middleware validates only what you tell it to. The secure
baseline:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,

            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30), // default is 5 MINUTES — shrink it

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
        };
        options.MapInboundClaims = false; // keep claim names as issued
    });
```

Findings, in severity order:
- `ValidateIssuerSigningKey = false` or no key set → **Critical**
  (forged tokens accepted).
- `ValidateLifetime = false` → **High** (stolen tokens never expire).
- `ValidateIssuer/ValidateAudience = false` → **High** in any
  environment with more than one token issuer/consumer; tokens minted
  for one service replay against another.
- `RequireHttpsMetadata = false` outside local dev → **Medium/High**.
- Algorithm: the handler rejects `alg: none` by design, but if you set
  `ValidAlgorithms`, allowlist exactly what you issue (`HS256` *or*
  `RS256`) — accepting both symmetric and asymmetric invites
  key-confusion attacks.

---

## §keys — Signing key management

- **Symmetric (HS256):** key must be ≥256 bits of real randomness — not
  a password, not `"GenWaveSecretKey2025"`. Anyone who can *read* the
  key can *mint* tokens; every service that validates can also forge.
  Fine for a single-issuer monolith; wrong once multiple services
  validate.
- **Asymmetric (RS256/ES256):** issuer holds the private key; validators
  get the public key. Required the moment a second consumer appears.
- Key source: environment variable / secret file / key vault. A signing
  key in `appsettings.json` in the repo is a **Critical** finding (and
  rotation, not deletion, is the fix).
- Rotation: support two acceptable keys during rollover
  (`IssuerSigningKeys` plural) so rotation isn't an outage.

---

## §lifetimes — Access and refresh tokens

- Access tokens: short — 5–15 minutes for an admin API. An hour-plus
  access token makes revocation meaningless.
- Refresh tokens: opaque random values (not JWTs), stored server-side
  (hashed, like passwords), bound to the user + client, single-use with
  rotation — a replayed refresh token revokes the whole family (theft
  detection).
- Logout / password change / role change must revoke refresh tokens.
  Role change matters in a SuperAdmin/Admin/User system: a demoted
  admin's access token stays valid until expiry — keep that window
  short, and for the demotion path specifically, consider a server-side
  denylist keyed by token `jti` for the remaining lifetime.

---

## §claims — What goes in the token

- Identity (`sub`), roles, and the minimum the API needs to authorize.
  Tokens are base64, not encrypted — no PII beyond necessity, never a
  secret.
- Authorize against claims the *server* issued, not fields the client
  posts alongside the token.
- Keep the role claim authoritative: if roles can change mid-session,
  either keep tokens short or re-check the DB for the sensitive
  endpoints (user management, config writes).

---

## §storage — Where the Admin UI keeps the token

This is the `security-web` skill's territory in detail, but the API
review must know the contract:
- `Authorization: Bearer` from memory is the baseline (XSS can read
  localStorage; prefer not to persist there).
- If cookies carry the token instead: `HttpOnly; Secure; SameSite=Lax`
  minimum — and then CSRF protection becomes the API's problem
  (SameSite + Origin verification on state-changing endpoints).
- Never accept the same credential from *both* header and cookie; the
  cookie path silently inherits CSRF exposure.

---

## §misconfigs — Common ASP.NET Core JWT findings checklist

- [ ] Default 5-minute `ClockSkew` left in place (usually acceptable —
      flag only with short-lived tokens where it doubles the lifetime).
- [ ] Token endpoint not rate-limited (see aspnetcore.md §abuse).
- [ ] `Audience`/`Issuer` values differ between issuance and validation
      (works only because validation is off — see §validation).
- [ ] Errors from token validation returned verbosely to the client
      (`options.Events.OnAuthenticationFailed` writing exception
      details) — generic 401 only.
- [ ] Tokens logged: grep logging for `Authorization`, `access_token`,
      `refresh_token`. Tokens in logs are credentials in logs.
- [ ] SignalR/websockets: token passed via query string
      (`access_token=`) ends up in proxy logs — acceptable only if logs
      are treated as secret-bearing, better via the dedicated
      `OnMessageReceived` hub path with short-lived tokens.
