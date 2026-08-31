// Client-side wire shape + fetcher for the station-level rotation-thumb endpoint (SPEC F150.1,
// F150.7, F150.8; STORY-370, PLAN T367/T369). Sibling of lib/persona-taste-api.ts's taste-thumb
// module — the two request bodies are structurally identical by coincidence, never by a shared
// type or write path (BoothLogController.ThumbStation's own remarks: this reaches IThumbStore
// only, never IPersonaTasteAccrualStore — GenWave.Architecture.Tests' disjointness pin proves it
// server-side). Browser fetches go through the Next.js same-origin rewrite (/api/* -> api:8080),
// same convention as lib/persona-taste-api.ts.

import { readErrorMessage } from "@/lib/problem-details";

export type StationThumbDirection = "up" | "down";

/** The four `StationThumbResponse.Result` tokens (SPEC F150.1, F150.8) — every one arrives as a
 * 200: even `"ignored"` (safe-scope or unknown media) is a successful, side-effect-free response
 * server-side, never a 4xx (`BoothLogController.ThumbStation`'s own remarks). */
export type StationThumbResult = "recorded" | "unchanged" | "flipped" | "ignored";

const STATION_THUMB_RESULTS: ReadonlySet<string> = new Set<StationThumbResult>([
  "recorded",
  "unchanged",
  "flipped",
  "ignored",
]);

/** Narrows an unknown wire value to a valid {@link StationThumbResult} (T369 review MED-4) — a
 * 200 whose body carries anything else (a future token this build doesn't know about yet, a
 * malformed body) is treated as a failure rather than indexed blindly into `RESULT_COPY` or
 * trusted as a settled direction. */
function isStationThumbResult(value: unknown): value is StationThumbResult {
  return typeof value === "string" && STATION_THUMB_RESULTS.has(value);
}

export interface StationThumbSuccess {
  ok: true;
  result: StationThumbResult;
}

export interface StationThumbFailure {
  ok: false;
  status: number;
  /** ProblemDetails `detail`, when the server sent one. `null` for a network failure (`status`
   * 0 — no response ever arrived to read one from) and for a 200 whose body didn't parse or
   * didn't validate as a {@link StationThumbResult} (T369 review MED-3/MED-4) — neither case has
   * a ProblemDetails body to read a detail from in the first place. */
  detail: string | null;
}

export type StationThumbOutcome = StationThumbSuccess | StationThumbFailure;

/** User-facing copy for a classified station-thumb failure (SPEC F31.3 posture, mirrors
 * lib/persona-taste-api.ts's describeTasteThumbFailure — same wording for the buckets the two
 * share). A 401 always reads as session-expiry regardless of whatever body the framework's own
 * auth challenge attached; a network failure gets the house network copy; 403/404 get the same
 * fixed copy every other mutation module in this directory uses; everything else (400 and
 * anything unclassified) prefers the server's own `detail` — for the 400 case it already names
 * the row's own kind (F150.8), app-authored vocabulary the operator can act on directly — falling
 * back to a generic message only when none arrived. */
export function describeStationThumbFailure(outcome: StationThumbFailure): string {
  switch (outcome.status) {
    case 0:
      return "Network error — check your connection.";
    case 401:
      return "Your session has expired — sign in again.";
    case 403:
      return "You don't have permission to make this change.";
    case 404:
      return "This booth-log row no longer exists.";
    default:
      return outcome.detail ?? `Unexpected error (${outcome.status})`;
  }
}

/**
 * POST /api/booth-log/{id}/station-thumb (SPEC F150.1, F150.7, F150.8) — nudges the STATION's own
 * rotation signal for the track aired on booth-log row `boothLogId`. Never throws: a non-2xx or
 * network failure resolves to a classified {@link StationThumbFailure} so the caller can toast a
 * distinct message and leave its rendered state untouched — the same never-throw contract every
 * other mutation module in this directory (`persona-taste-api.ts`, `announcements-api.ts`) uses.
 *
 * The 200 body is parsed and validated INSIDE the same try as the fetch (T369 review MED-3): a
 * non-JSON or off-schema 200 body degrades to a classified failure exactly like a non-2xx status
 * would, rather than throwing out of this "never-throw" module and leaving the caller's pending
 * state stuck forever (the bug a bare `await response.json()` after the `try` block already
 * closed would reintroduce).
 */
export async function postStationThumb(
  boothLogId: number,
  direction: StationThumbDirection
): Promise<StationThumbOutcome> {
  let response: Response;
  try {
    response = await fetch(`/api/booth-log/${boothLogId}/station-thumb`, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ direction }),
    });
  } catch {
    return { ok: false, status: 0, detail: null };
  }
  if (!response.ok) {
    return { ok: false, status: response.status, detail: await readErrorMessage(response) };
  }
  try {
    const raw = (await response.json()) as unknown;
    const result = typeof raw === "object" && raw !== null ? (raw as Record<string, unknown>)["result"] : undefined;
    if (!isStationThumbResult(result)) {
      return { ok: false, status: response.status, detail: null };
    }
    return { ok: true, result };
  } catch {
    return { ok: false, status: response.status, detail: null };
  }
}
