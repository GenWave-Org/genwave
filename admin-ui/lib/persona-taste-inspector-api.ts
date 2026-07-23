// Client-side wire shapes + fetcher for the persona taste inspector (SPEC F86.6-F86.7, STORY-219,
// PLAN T77/T78). Browser fetches go through the Next.js same-origin rewrite (/api/* -> api:8080),
// same convention as lib/booth-log-api.ts and lib/persona-taste-api.ts (that file's own
// taste-thumb POST is a different endpoint entirely — this one is the read-only inspector GET,
// never a mutation).

/** One `station.persona_taste` row (SPEC F86.6) — mirrors `PersonaTasteRuleDto`
 * (src/GenWave.Host/Api/PersonaTasteRuleDto.cs). `daysOfWeek` is `System.DayOfWeek`'s own wire
 * encoding (0 = Sunday … 6 = Saturday; no `JsonStringEnumConverter` is registered on the host).
 * An empty `daysOfWeek` with both hours `null` means "no gate" — the same unbounded-field
 * convention the DTO's own remarks document; there is no separate "gated: bool" to drift out of
 * sync with the three fields it would summarize. `weight` keeps its sign (dislikes are taste too,
 * SPEC F82.1) and carries float→double wire noise (e.g. `0.800000011920929`) — round for display,
 * never for the value itself. */
export interface PersonaTasteRule {
  predicateSummary: string;
  daysOfWeek: number[];
  startHour: number | null;
  endHour: number | null;
  weight: number;
  updatedAt: string;
}

/** `GET /api/personas/{id}/taste` response (SPEC F86.6) — mirrors `PersonaTasteResponseDto`.
 * `accruedCount`/`accruedCap` are the server's own count-against-the-cap (F84.3) so the cap meter
 * never hardcodes a copy of that number. */
export interface PersonaTasteResponse {
  authored: PersonaTasteRule[];
  operator: PersonaTasteRule[];
  accrued: PersonaTasteRule[];
  accruedCount: number;
  accruedCap: number;
}

/**
 * GET /api/personas/{id}/taste (SPEC F86.6, AdminOnly) — the persona's taste rules grouped by
 * source plus the accrued cap-meter fields. Read-only: this module exposes no companion write
 * function, mirroring the endpoint's own no-mutation-route contract.
 */
export async function fetchPersonaTaste(personaId: number): Promise<PersonaTasteResponse> {
  const response = await fetch(`/api/personas/${personaId}/taste`, {
    credentials: "include",
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`GET /api/personas/${personaId}/taste failed: ${response.status}`);
  }
  return (await response.json()) as PersonaTasteResponse;
}
