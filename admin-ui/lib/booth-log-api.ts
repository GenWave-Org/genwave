// Client-side wire shapes + fetcher for the booth log's narrative feed (PLAN T40, STORY-195,
// SPEC F72.1-F72.2). Browser fetches go through the Next.js same-origin rewrite (/api/* ->
// api:8080), same convention as lib/broadcast-api.ts — never lib/api.ts's apiGet, which is
// server-only.

/** One narrative row (SPEC F72.1) — `kind` is a plain string on the wire (BoothLogEntryDto), not
 * a closed union: the admin UI must keep rendering a row for a kind it doesn't specifically
 * style rather than drop it, so a future event type never silently vanishes from the feed. */
export interface BoothLogEntry {
  occurredAt: string;
  kind: string;
  summary: string;
  /**
   * The row's own DB id (SPEC F84.1, F84.6; STORY-215, PLAN T71) — the wire's airing identity:
   * the taste-thumb POST target (`POST /api/booth-log/{id}/taste-thumb`) for this row directly,
   * and (via the latest track-started row) for the now-playing surface too. Also the stable React
   * key `BoothLogFeed` renders each row by — `occurredAt`+index would shift under a new head-page
   * poll and remount every row's `PersonaTasteThumbs`, resetting its settled thumb state.
   *
   * Emitted by `BoothLogEntryDto` (src/GenWave.Host/Api/BoothLogEntryDto.cs) as of this same task
   * (T71) — the domain `BoothLogEntry` record has always carried `Id`; the DTO/controller now
   * include it alongside `PersonaId` (added in T60).
   */
  id: number;
  /** Persona stamped on air for a track-start row (SPEC F84.6, added in T60); `null`/absent for
   * every other kind, a persona-less airing, or a row that predates the column. Gates whether this
   * row's taste-thumb control renders at all — a row without one offers no control, not a
   * disabled one (F84.6). */
  personaId: number | null;
}

/** One newest-first keyset page (SPEC F72.2) — `nextBefore` is `null` once this is the oldest page. */
export interface BoothLogPage {
  entries: BoothLogEntry[];
  nextBefore: string | null;
}

/**
 * GET /api/booth-log?before=&take= (SPEC F72.2). `before` is the opaque cursor from a previous
 * response's `nextBefore`; omitted for the newest page. `take` is left to the endpoint's own
 * default (50) — the admin UI never overrides it.
 */
export async function fetchBoothLogPage(before?: string): Promise<BoothLogPage> {
  const url = before ? `/api/booth-log?before=${encodeURIComponent(before)}` : "/api/booth-log";
  const response = await fetch(url, {
    credentials: "include",
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`GET /api/booth-log failed: ${response.status}`);
  }
  return (await response.json()) as BoothLogPage;
}
