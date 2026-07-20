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
