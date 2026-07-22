// Client-side wire shapes + fetcher for the persona taste-thumb endpoint (SPEC F84.1, F84.5-
// F84.7; STORY-215, PLAN T70/T71). One route shape serves BOTH the now-playing and booth-log
// admin surfaces (BoothLogController's own doc comment) — the credited persona is whichever one
// is stamped on the booth-log row at air time, resolved BEFORE this call ever fires (the
// now-playing surface's resolution step lives in app/(authed)/live/useNowPlayingTasteAttribution.ts).
// Browser fetches go through the Next.js same-origin rewrite (/api/* -> api:8080), same
// convention as lib/broadcast-api.ts and lib/booth-log-api.ts — never lib/api.ts's apiGet, which
// is server-only.

export type TasteThumbDirection = "up" | "down";

/** Failure buckets a taste-thumb POST classifies a non-2xx/network outcome into (SPEC F31.3
 * posture, mirrors lib/broadcast-api.ts's RatingFailureKind). `not-thumbable` is the F84.6 400 —
 * unstamped row, non-track row, or no known artist — surfaced with the backend's own explanatory
 * `detail` rather than fixed copy, since the three cases read very differently to an operator. */
export type TasteThumbFailureKind =
  | "not-thumbable"
  | "unauthorized"
  | "forbidden"
  | "not-found"
  | "network"
  | "unknown";

interface TasteThumbFailure {
  ok: false;
  kind: TasteThumbFailureKind;
  status: number | null;
  /** ProblemDetails `detail`, when the server sent one. `null` for network failures and any
   * response whose body didn't parse as ProblemDetails. */
  detail: string | null;
}

export interface TasteThumbSuccess {
  ok: true;
  /** True when this exact (persona, row, direction) was already recorded — the weight did not
   * move again (F84.5 idempotency). The control settles to the same disabled-direction state
   * either way (T71's idempotency affordance) — callers don't need to branch on this to decide
   * what to render, only whether to keep it in mind for copy. */
  alreadyRecorded: boolean;
  /** Raw clamped weight after the nudge; `null` on an already-recorded outcome (nothing moved).
   * Carried as a raw float on the wire (T70 review note, carried to T71) — round before display. */
  weight: number | null;
}

export type TasteThumbOutcome = TasteThumbSuccess | TasteThumbFailure;

function classifyTasteThumbStatus(status: number): TasteThumbFailureKind {
  switch (status) {
    case 400:
      return "not-thumbable";
    case 401:
      return "unauthorized";
    case 403:
      return "forbidden";
    case 404:
      return "not-found";
    default:
      return "unknown";
  }
}

/** Best-effort ProblemDetails `detail` reader — a malformed/empty/non-JSON body degrades to
 * `null` rather than throwing past this function. */
async function readDetail(response: Response): Promise<string | null> {
  try {
    const raw = (await response.json()) as unknown;
    if (typeof raw === "object" && raw !== null) {
      const detail = (raw as Record<string, unknown>)["detail"];
      if (typeof detail === "string" && detail !== "") return detail;
    }
  } catch {
    // malformed or empty body — no detail to surface
  }
  return null;
}

/** User-facing copy for a classified taste-thumb failure (SPEC F31.3). `not-thumbable` prefers
 * the server's own `detail` — it already explains which of the three F84.6 reasons applied — and
 * only falls back to fixed copy when the body carried none; every other bucket shares its wording
 * with the rating controls' own failure copy (lib/broadcast-api.ts's describeRatingFailure). */
export function describeTasteThumbFailure(outcome: TasteThumbFailure): string {
  switch (outcome.kind) {
    case "not-thumbable":
      return outcome.detail ?? "This row can't be thumbed for taste.";
    case "unauthorized":
      return "Your session has expired — sign in again.";
    case "forbidden":
      return "You don't have permission to make this change.";
    case "not-found":
      return "This booth-log row no longer exists.";
    case "network":
      return "Network error — check your connection.";
    case "unknown":
      return `Unexpected error (${outcome.status ?? "?"})`;
  }
}

/**
 * POST /api/booth-log/{id}/taste-thumb (F84.1, F84.5, F84.6) — nudges the accrued artist rule for
 * whichever persona was stamped on `boothLogRowId` at air time. Never throws: a non-2xx or
 * network failure resolves to a classified {@link TasteThumbFailure} so the caller can toast a
 * distinct message and leave its displayed state untouched — the same contract
 * lib/broadcast-api.ts's `voteTrack`/`setNeverPlay` already use.
 */
export async function postTasteThumb(
  boothLogRowId: number,
  direction: TasteThumbDirection
): Promise<TasteThumbOutcome> {
  let response: Response;
  try {
    response = await fetch(`/api/booth-log/${boothLogRowId}/taste-thumb`, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ direction }),
    });
  } catch {
    return { ok: false, kind: "network", status: null, detail: null };
  }
  if (!response.ok) {
    return {
      ok: false,
      kind: classifyTasteThumbStatus(response.status),
      status: response.status,
      detail: await readDetail(response),
    };
  }
  const body = (await response.json()) as { alreadyRecorded: boolean; weight: number | null };
  return { ok: true, alreadyRecorded: body.alreadyRecorded, weight: body.weight };
}
