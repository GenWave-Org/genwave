# Finding report format

Every reported issue uses this shape. No exploit path → label it
**Observation**, not Finding, and skip Severity.

---

### [SEVERITY] Short title (vuln class)

- **Severity:** Critical | High | Medium | Low — and the one assumption
  that makes it that (e.g. "assumes the endpoint is reachable by plain
  Users, which it is — no role policy on this controller").
- **Location:** `path/to/File.cs:42` (and the sink/source if split
  across files).
- **Class:** IDOR / injection / authz / mass-assignment / SSRF /
  secret-exposure / path-traversal / …
- **Exploit:** concrete walkthrough. Who the attacker is, the exact
  request/input they send, what they get. A real payload, not "could be
  abused". If you can't write this, it's an Observation.
- **Fix:** the minimal change, with the corrected snippet. Reference the
  rule (`SKILL.md` rule N) and the secure default (`templates/…`) —
  don't invent a one-off if a standard fix exists.
- **Confidence:** High (traced) | Medium (likely, unverified path) |
  needs-confirmation (and what to check).

---

### Example

### [High] Play-history IDOR via `stationId` (authz / IDOR)

- **Severity:** High — any authenticated User can read any station's
  play history and listener analytics; ownership is never checked.
- **Location:** `API/GenWave.Manager/Controllers/AnalyticsController.cs:58`
  (query), `[Authorize]` only at class level.
- **Class:** Broken object-level authorization (IDOR).
- **Exploit:** Log in as a plain User, `GET /api/analytics/play-history?stationId=12`
  with an incremented id. The query is
  `Where(p => p.StationId == stationId)` with no principal scope, so it
  returns station 12's history regardless of who owns it —
  cross-account analytics disclosure.
- **Fix:** scope the query to the session principal:
  `Where(p => p.StationId == stationId && p.Station.OwnerId == userId)`
  and return 404 on miss (don't confirm existence). See
  `aspnetcore.md` §authz, SKILL.md rule 1.
- **Confidence:** High — traced source→sink, no intervening policy check.
