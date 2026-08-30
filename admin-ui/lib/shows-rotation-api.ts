// Client-side wire shapes + fetchers for a show's own "deep cuts" rotation rule (SPEC F152.3,
// F152.5, STORY-373, PLAN T362) — GET/PUT /api/shows/{id}[/rotation-pool|/last-airing], the SAME
// endpoint family ShowsController/ShowRotationController serve. This module is the ONE source for
// every rotation wire shape (T362 review LOW-7: RotationPredicateDto used to be declared twice,
// here AND in app/(authed)/shows/types.ts — collapsed to this module's own export, imported from
// there instead) — app/ imports FROM lib/ as usual; the "never import the other direction" rule
// (mirrors announcements-api.ts's own posture) only ever barred lib/ reaching INTO app/. Browser
// fetches go through the Next.js same-origin rewrite, mirroring every other *-api.ts module in this
// folder.

import { readErrorMessage } from "@/lib/problem-details";

/** A show's own "deep cuts" rotation rule (SPEC F152.1) — mirrors
 * `GenWave.Abstractions.Playout.RotationPredicate` field for field. Both members are independently
 * optional on the wire, but the server rejects a save where both are `null` (SPEC F152.5: at least
 * one bound). The canonical source — `ShowDto.rotation` (app/(authed)/shows/types.ts) imports this
 * type rather than redeclaring it. */
export interface RotationPredicateDto {
  maxPlays: number | null;
  notAiredWithinDays: number | null;
}

/** `GET /api/shows/{id}/rotation-pool` (SPEC F152.5) — the live pool size chip. `eligible` is
 * `null` ("unknown") when the catalog can't answer (an empty rotation scope); `since` is the
 * rotation ledger's own epoch (`Gardener:RotationSince`), `null` only on a pre-Gardener install. */
export interface ShowRotationPoolDto {
  eligible: number | null;
  since: string | null;
}

/** Never throws: a network failure or non-2xx resolves to `null` so the pool chip can render
 * "unknown" instead of an unhandled rejection (mirrors `usePoll`'s own never-throws contract one
 * layer up, and `fetchAnnouncementHistory`'s identical posture one module over). */
export async function fetchShowRotationPool(id: number): Promise<ShowRotationPoolDto | null> {
  try {
    const response = await fetch(`/api/shows/${id}/rotation-pool`, { credentials: "include", cache: "no-store" });
    if (!response.ok) return null;
    return (await response.json()) as ShowRotationPoolDto;
  } catch {
    return null;
  }
}

/** `GET /api/shows/{id}/last-airing` (SPEC F152.5) — ALWAYS a 200 body (T362 review MED-3: an
 * earlier draft answered a bare JSON `null`/204 for "never aired," which `response.json()` cannot
 * parse). `airedCount`/`relaxed` both `null` together means the show has never aired a track yet
 * (T362 review LOW-6: renamed from an earlier `picks`-named field — see the server DTO's own
 * remarks for why). This fetcher's own `null` RETURN VALUE means something different: a network
 * failure or non-2xx response, never a valid "never aired" answer. */
export interface ShowLastAiringDto {
  airedCount: number | null;
  relaxed: number | null;
}

/** Never throws — a network failure or non-2xx resolves to `null`; the last-airing line reads that
 * exactly like a genuine "never aired" body (both degrade to "nothing to show"). */
export async function fetchShowLastAiring(id: number): Promise<ShowLastAiringDto | null> {
  try {
    const response = await fetch(`/api/shows/${id}/last-airing`, { credentials: "include", cache: "no-store" });
    if (!response.ok) return null;
    return (await response.json()) as ShowLastAiringDto;
  } catch {
    return null;
  }
}

/** The rule editor's own combined poll read (one `usePoll` tick, two GETs in parallel) — the pool
 * chip and the last-airing line update together at the same gentle cadence. */
export interface ShowRotationStatus {
  pool: ShowRotationPoolDto | null;
  lastAiring: ShowLastAiringDto | null;
}

export async function fetchShowRotationStatus(id: number): Promise<ShowRotationStatus> {
  const [pool, lastAiring] = await Promise.all([fetchShowRotationPool(id), fetchShowLastAiring(id)]);
  return { pool, lastAiring };
}

export type SaveRotationOutcome =
  | { ok: true; rotation: RotationPredicateDto | null }
  | { ok: false; detail: string };

/** `PUT /api/shows/{id}` (SPEC F152.5) — the ONE write path for a show's rotation rule.
 * `rotation: null` clears it; this module always sends an explicit value (never omits the property)
 * — the editor always knows what it wants the rule to become, so the server's own "absent = leave
 * unchanged" case never applies here. */
export async function saveShowRotation(
  id: number,
  rotation: RotationPredicateDto | null
): Promise<SaveRotationOutcome> {
  let response: Response;
  try {
    response = await fetch(`/api/shows/${id}`, {
      method: "PUT",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ rotation }),
    });
  } catch {
    return { ok: false, detail: "Network error — check your connection." };
  }
  if (!response.ok) {
    return { ok: false, detail: await readErrorMessage(response) };
  }
  const body = (await response.json()) as { rotation: RotationPredicateDto | null };
  return { ok: true, rotation: body.rotation };
}
