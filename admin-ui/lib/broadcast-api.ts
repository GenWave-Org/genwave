// Client-side wire shapes + fetchers for live broadcast state (SPEC
// F28.6–F28.8). Shared by the Dashboard (Q5) and Live (Q6) pages — both poll
// these through lib/use-poll.ts. Browser fetches go through the Next.js
// same-origin rewrite (/api/* -> api:8080), never lib/api.ts's apiGet, which
// is server-only (it forwards a caller-supplied cookie header that only
// exists in a Server Component/Route Handler request context).

import type { GardenerStatusSummary } from "@/lib/gardener-api";

interface NowPlayingTrackWire {
  stationId: string;
  mediaId: string;
  /**
   * "patter" for a `tts:*` DJ break, "track" otherwise — stamped by LiveController (gh-#187).
   * Optional on the wire for one deploy of backward tolerance; fetchNowPlaying falls back to the
   * `tts:` mediaId prefix (the same cross-layer convention LiveView's rating gate already uses).
   */
  kind?: "track" | "patter";
  title?: string;
  artist?: string;
  gainDb: number;
  startedAt: string;
  /** Nullable — engine-initiated plays (safe rotation, engine echo) carry no duration (SPEC F50.2). */
  durationMs?: number | null;
}

interface NowPlayingDrainWire {
  stationId: string;
  drain: true;
}

type NowPlayingWire = NowPlayingTrackWire | NowPlayingDrainWire;

/**
 * The now-playing card's renderable states (F16.5's track / drain / 503 trio, plus gh-#187's
 * patter split). A patter carries the same wire fields as a track — but its `title` is literally
 * the station name (TtsSegmentSource), which is exactly why it renders differently: `artist` holds
 * the persona name and is the only field worth displaying.
 */
export type NowPlayingState =
  | ({ kind: "track" } & Omit<NowPlayingTrackWire, "kind">)
  | ({ kind: "patter" } & Omit<NowPlayingTrackWire, "kind">)
  | { kind: "drain"; stationId: string }
  | { kind: "warming" };

export interface StatusResponse {
  startedAt: string;
  catalog: {
    ready: number;
    enriching: number;
    failed: number;
    unavailable: number;
  };
  safeScope: {
    libraryIds: number[];
    playable: number;
  };
  /** SPEC F34.8, STORY-125 — LLM copy-writer health: config + last on-air attempt, no live polling. */
  llm: {
    enabled: boolean;
    model: string | null;
    activePersona: string | null;
    lastOutcome: "ok" | "failed" | null;
    lastAttemptAt: string | null;
    /**
     * SPEC F139.2, STORY-353, PLAN T334 — the F139 cause taxonomy's own read of "why is the tile
     * red": the highest-count non-success cause GenWave.Tts.LlmCallCauseCounters recorded for
     * Copy-kind calls in the rolling 24h window, plus its count and the model it was recorded
     * against. All three are `null` together whenever there is nothing to explain (nothing but
     * Success recorded, or nothing at all) — the same "quiet is not a fault" posture `lastOutcome`
     * above already follows. Optional on the wire (PLAN T334 adds these three fields after several
     * other spec files already built their own `StatusResponse` fixture literals): every fixture
     * that omits them still satisfies this type, so this addition never forces an edit onto a file
     * this task doesn't otherwise touch — mirrors `NowPlayingTrackWire.kind`'s own "one deploy of
     * backward tolerance" convention above, minus the deploy: this is a same-task compatibility
     * choice, not a rollout one.
     */
    dominantCause?: string | null;
    dominantCauseCount?: number | null;
    dominantCauseModel?: string | null;
  };
  /**
   * SPEC F99.5, F100.3, STORY-256 AC4 — the primary voice engine's own cached health verdict.
   * Answers ONLY "is the engine down"; `llm.lastOutcome === "failed"` above answers "does the DJ
   * have anything to say" — the two tiles let an operator tell the two causes of a quiet DJ apart
   * without a log stack.
   */
  voice: {
    engine: string;
    degraded: boolean;
    reason: string | null;
    checkedAt: string | null;
  };
  /**
   * SPEC F149.5, STORY-368, PLAN T371 — the rotation-health aggregate: how much of the playable
   * catalog has never aired, aired exactly once, or gone stale (90+ days since its last airing),
   * plus the ledger's own epoch. Optional on the wire (added after several other spec files already
   * built their own `StatusResponse` fixture literals, mirroring `llm.dominantCause`'s own "one
   * deploy of backward tolerance" convention above) — every fixture that omits it still satisfies
   * this type. `rotationSince` is `null` only on a pre-Gardener install whose migration has never
   * run.
   */
  rotation?: {
    playable: number;
    neverAired: number;
    airedOnce: number;
    notAiredDays90: number;
    rotationSince: string | null;
  };
  /**
   * SPEC F153.9, STORY-374 AC8, PLAN T378 — the Library Gardener's own per-kind OPEN totals plus
   * the grand total, backing the dashboard's Gardener tile. Optional on the wire, the same "one
   * deploy of backward tolerance" convention `rotation`/`llm.dominantCause` above already follow —
   * every fixture literal built before this task shipped still satisfies this type.
   */
  gardener?: GardenerStatusSummary;
}

export interface PlayHistoryEntry {
  mediaId: string;
  title?: string;
  artist?: string;
  gainDb: number;
  startedAt: string;
  endedAt?: string;
  /** Nullable — engine-initiated plays and `tts:*` patter entries carry no duration (SPEC F50.2, F50.6). */
  durationMs?: number | null;
}

/** One catalog media id's rating state (SPEC F33.2, F33.9) — the `GET /api/ratings` element shape. */
export interface RatingEntry {
  mediaId: string;
  score: number;
  neverPlay: boolean;
  /** gh-#99 — `false` for safe-scope content (safe-loop tracks, station IDs): render NO vote or
   * never-play control at all, not a disabled one. Optional so a cached/older API shape (absent
   * field) keeps the pre-#99 behavior: everything rateable. */
  rateable?: boolean;
}

/** gh-#99 — the one gate every rating surface shares: an entry is rateable unless the server
 * said otherwise. `undefined` (no entry fetched yet, or an older API) stays rateable — the write
 * endpoints refuse safe content independently, so this is presentation, not enforcement. */
export function isRateable(entry: RatingEntry | undefined): boolean {
  return entry?.rateable !== false;
}

export type VoteDirection = "up" | "down";

/**
 * Failure buckets a rating mutation classifies a non-2xx/network outcome into (SPEC F31.3
 * posture, reused for copy only — rating writes carry no `useRowPatch`/ETag machinery, PLAN.md
 * Epic S sequencing note). No `conflict`/`unsupported-media-type` bucket: a vote is an atomic
 * clamped increment and a never-play set is idempotent (F33.3/F33.4) — neither has an `If-Match`
 * to violate.
 */
export type RatingFailureKind = "unauthorized" | "forbidden" | "not-found" | "network" | "unknown";

/**
 * User-facing copy for a classified rating-mutation failure (SPEC F31.3) — the single source of
 * this wording, shared verbatim by every rating control (`RatingControls` on the Live page,
 * `NeverPlayControl` and `ExplicitOverrideControl` on the Catalog page, STORY-114/STORY-115/
 * STORY-251) so all three surfaces can't drift apart on the same failure.
 */
export function describeRatingFailure(kind: RatingFailureKind, status: number | null): string {
  switch (kind) {
    case "unauthorized":
      return "Your session has expired — sign in again.";
    case "forbidden":
      return "You don't have permission to make this change.";
    case "not-found":
      return "This track no longer exists.";
    case "network":
      return "Network error — check your connection.";
    case "unknown":
      return `Unexpected error (${status ?? "?"})`;
  }
}

interface RatingFailure {
  ok: false;
  kind: RatingFailureKind;
  status: number | null;
}

export interface VoteSuccess {
  ok: true;
  score: number;
}
export type VoteOutcome = VoteSuccess | RatingFailure;

export interface NeverPlaySuccess {
  ok: true;
  neverPlay: boolean;
}
export type NeverPlayOutcome = NeverPlaySuccess | RatingFailure;

/** Who stamped an explicit/advisory classification (SPEC F95.2/F95.3) — the automated tag pass,
 * an LLM sweep, or an operator override via {@link setExplicitOverride}. Named so the wire's
 * three-value vocabulary has exactly one definition, shared by {@link ExplicitOverrideSuccess}
 * and the catalog `AdminMediaDto.explicitSource` (`app/(authed)/catalog/types.ts`) instead of each
 * repeating — and risking drifting from — the same string-literal union. */
export type ExplicitSource = "tag" | "llm" | "operator";

export interface ExplicitOverrideSuccess {
  ok: true;
  explicit: boolean | null;
  explicitSource: ExplicitSource | null;
}
export type ExplicitOverrideOutcome = ExplicitOverrideSuccess | RatingFailure;

/** A media id this UI can vote/flag by id — the `RatingController` route requires a numeric
 * `long`; `tts:*` (and any other non-numeric) id would 404 the route entirely, so this same
 * check both selects which ids are worth batch-reading (F33.9's "non-numeric ids skipped
 * silently" is enforced server-side too, but an empty id list must never be requested at all)
 * and which rows/cards get rating controls rendered at all (F33.11). */
export function isCatalogMediaId(mediaId: string): boolean {
  return /^\d+$/.test(mediaId);
}

function classifyRatingStatus(status: number): RatingFailureKind {
  switch (status) {
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

/**
 * GET /api/ratings?ids=… (F33.9) — batch rating read composed on the poll cadence from whatever
 * catalog ids are currently visible. Filters to numeric catalog ids client-side first: an empty
 * result (every visible id is `tts:*` or there's nothing on screen yet) MUST NOT issue a request.
 */
export async function fetchRatings(mediaIds: readonly string[]): Promise<RatingEntry[]> {
  const catalogIds = mediaIds.filter(isCatalogMediaId);
  if (catalogIds.length === 0) {
    return [];
  }
  const response = await fetch(`/api/ratings?ids=${catalogIds.join(",")}`, {
    credentials: "include",
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`GET /api/ratings failed: ${response.status}`);
  }
  return (await response.json()) as RatingEntry[];
}

/**
 * Shared fetch/try-catch/classify/parse idiom every rating-style write (`voteTrack`,
 * `setNeverPlay`, `setExplicitOverride`) follows: POST/PUT a JSON `body`, a network failure or a
 * non-2xx response both resolve to a classified {@link RatingFailure} — never throws, the whole
 * point of the idiom — and a 2xx response's JSON is handed to `parseSuccess` to build the caller's
 * specific success shape. One definition so the three call sites can't drift apart on it.
 */
async function writeRatingMutation<T extends object>(
  path: string,
  method: "POST" | "PUT",
  body: unknown,
  parseSuccess: (json: unknown) => T
): Promise<({ ok: true } & T) | RatingFailure> {
  let response: Response;
  try {
    response = await fetch(path, {
      method,
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
  } catch {
    return { ok: false, kind: "network", status: null };
  }
  if (!response.ok) {
    return { ok: false, kind: classifyRatingStatus(response.status), status: response.status };
  }
  return { ok: true, ...parseSuccess(await response.json()) };
}

/** POST /api/media/{id}/vote (F33.3) — ±1 clamped to [0,100], applied atomically server-side.
 * Never throws: a non-2xx or network failure resolves to a classified `RatingFailure` so the
 * caller can toast a distinct message and leave its displayed score untouched. */
export async function voteTrack(mediaId: string, direction: VoteDirection): Promise<VoteOutcome> {
  return writeRatingMutation(`/api/media/${mediaId}/vote`, "POST", { direction }, (json) => {
    const body = json as { score: number };
    return { score: body.score };
  });
}

/** PUT /api/media/{id}/never-play (F33.4) — idempotent flag set. Never throws, same failure
 * contract as {@link voteTrack}. */
export async function setNeverPlay(mediaId: string, neverPlay: boolean): Promise<NeverPlayOutcome> {
  return writeRatingMutation(`/api/media/${mediaId}/never-play`, "PUT", { neverPlay }, (json) => {
    const body = json as { neverPlay: boolean };
    return { neverPlay: body.neverPlay };
  });
}

/**
 * PUT /api/media/{id}/explicit (SPEC F95.3, F95.5, STORY-251, PLAN T115/T116) — operator override
 * of the explicit/advisory classification. Same wire contract as {@link setNeverPlay}: idempotent,
 * no If-Match, tri-state body `{ explicit: true | false | null }` — `null` clears the row back to
 * unknown/unclassified, releasing it to the tag pass or the next LLM sweep. Never throws, same
 * failure contract as {@link voteTrack}.
 */
export async function setExplicitOverride(
  mediaId: string,
  explicitValue: boolean | null
): Promise<ExplicitOverrideOutcome> {
  return writeRatingMutation(
    `/api/media/${mediaId}/explicit`,
    "PUT",
    { explicit: explicitValue },
    (json) => {
      const body = json as { explicit: boolean | null; explicitSource: ExplicitSource | null };
      return { explicit: body.explicit, explicitSource: body.explicitSource };
    }
  );
}

function isDrainWire(body: NowPlayingWire): body is NowPlayingDrainWire {
  return "drain" in body && body.drain === true;
}

/**
 * GET /api/now-playing. A 503 (feeder hasn't ticked yet) resolves to the
 * `"warming"` state rather than rejecting — it is an expected, modelled
 * state, distinct from a poll failure (which usePoll surfaces via `error`).
 */
export async function fetchNowPlaying(): Promise<NowPlayingState> {
  const response = await fetch("/api/now-playing", {
    credentials: "include",
    cache: "no-store",
  });

  if (response.status === 503) {
    return { kind: "warming" };
  }
  if (!response.ok) {
    throw new Error(`GET /api/now-playing failed: ${response.status}`);
  }

  const body = (await response.json()) as NowPlayingWire;
  if (isDrainWire(body)) {
    return { kind: "drain", stationId: body.stationId };
  }
  const isPatter = body.kind === "patter" || body.mediaId.startsWith("tts:");
  return { ...body, kind: isPatter ? "patter" : "track" };
}

export async function fetchStatus(): Promise<StatusResponse> {
  const response = await fetch("/api/status", {
    credentials: "include",
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`GET /api/status failed: ${response.status}`);
  }
  return (await response.json()) as StatusResponse;
}

/** GET /api/play-history — the ring's contents, newest first (F16.4); empty array when nothing has aired. */
export async function fetchPlayHistory(): Promise<PlayHistoryEntry[]> {
  const response = await fetch("/api/play-history", {
    credentials: "include",
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`GET /api/play-history failed: ${response.status}`);
  }
  return (await response.json()) as PlayHistoryEntry[];
}
