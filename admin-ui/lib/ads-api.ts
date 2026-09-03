// Client-side wire shapes + fetchers for the Ads admin surface (SPEC F162.1; STORY-392; PLAN T404)
// — GET /api/ads (paged, state-scoped), POST /api/ads, PATCH /api/ads/{id}, POST
// /api/ads/{id}/approve|retry|retire, GET/POST /api/ad-briefs, PATCH /api/ad-briefs/{id}. Mirrors
// gardener-api.ts's own convention (the ONE source for every Ads wire shape) rather than widening
// broadcast-api.ts, which is scoped to live-broadcast state (now-playing/status/ratings) — a
// separate feature-shaped module keeps the two from coupling on an unrelated read (the
// shows-rotation-api.ts/gardener-api.ts precedent this folder already holds for every other
// feature). Browser fetches go through the Next.js same-origin rewrite, same as every other
// *-api.ts module here; the page's own server-rendered GETs (`apiGet`, forwarding the request
// cookie) build their paths from the exported `build*Path` helpers below rather than calling fetch
// directly, mirroring `buildGardenerFindingsPath`.

import { readProblemDetails } from "@/lib/problem-details";

/** The six-state `station.ad_spot` machine (SPEC F159.2, `GenWave.Core.Domain.AdStateTokens`) —
 * lowercase machine tokens, verbatim off the wire, never re-cased. */
export const AD_STATE_TOKENS = [
  "draft",
  "approved",
  "rendering",
  "ready",
  "failed",
  "retired",
] as const satisfies readonly string[];
export type AdState = (typeof AD_STATE_TOKENS)[number];

/** Section header copy — sentence-cased per Dean's copy rule. */
export const AD_STATE_LABELS: Record<AdState, string> = {
  draft: "Draft",
  approved: "Approved",
  rendering: "Rendering",
  ready: "Ready",
  failed: "Failed",
  retired: "Retired",
};

/** One-line, state-named empty copy (the Gardener `GARDENER_KIND_EMPTY_LABELS` precedent) — reads
 * as "the right tab loaded, there's just nothing here" rather than a generic "Nothing here." */
export const AD_STATE_EMPTY_LABELS: Record<AdState, string> = {
  draft: "No draft spots.",
  approved: "No approved spots.",
  rendering: "Nothing rendering.",
  ready: "No ready spots.",
  failed: "No failed spots.",
  retired: "No retired spots.",
};

/** One `ad_spot.voice_plan` entry — `GenWave.Ads.AdVoicePlanEntry`'s exact wire shape (also what
 * `AdRenderService.ParseVoicePlan` reads back). */
export interface AdVoicePlanEntry {
  tag: string;
  voiceId: string;
  pace: number;
}

/** `AdSourceTokens`' own wire tokens (`GenWave.Core.Domain.AdSourceTokens`) — who authored a spot. */
export type AdSource = "llm" | "owner" | "pack";

/** Display copy for {@link AdSource} (Dean's copy rule — capitals; the `AD_STATE_LABELS` precedent
 * one map up). "LLM" stays an uppercase acronym, the ordinary English convention for one, not a
 * violation of it. */
export const AD_SOURCE_LABELS: Record<AdSource, string> = {
  owner: "Owner",
  llm: "LLM",
  pack: "Pack",
};

/** `AdSpotDto`'s exact wire shape (`GenWave.Host.Api.AdSpotDto`, SPEC F162.1; STORY-392; PLAN
 * T403). `version` is the bare xmin token — the same `If-Match: W/"<version>"` convention
 * `lib/use-row-patch.ts` already holds for `/api/media/{id}`, applied here for every Ads verb. */
export interface AdSpotDto {
  id: number;
  brand: string;
  title: string;
  brief: string | null;
  script: string | null;
  source: AdSource;
  packSlug: string | null;
  spotSeconds: number;
  voicePlan: AdVoicePlanEntry[] | null;
  bedMediaId: number | null;
  state: AdState;
  failReason: string | null;
  mediaId: number | null;
  createdAt: string;
  stateChangedAt: string;
  renderedAt: string | null;
  retiredAt: string | null;
  version: string;
}

/** `GET /api/ads`'s own `{ items, total }` envelope (`AdsController.List`, the
 * `GardenerController.GetFindings` paging idiom). */
export interface AdsListResponse {
  items: AdSpotDto[];
  total: number;
}

/** `GET /api/ads?state=&limit=&offset=` (SPEC F162.1) — always state-scoped (the page never lists
 * "any state" — each tab is exactly one). Mirrors `buildGardenerFindingsPath`. */
export function buildAdsListPath(state: AdState, limit: number, offset: number): string {
  const query = new URLSearchParams();
  query.set("state", state);
  query.set("limit", String(limit));
  query.set("offset", String(offset));
  return `/api/ads?${query.toString()}`;
}

/** `AdBriefDto`'s exact wire shape (`GenWave.Host.Api.AdBriefDto`, SPEC F162.1/F162.2; PLAN
 * T403b). `packSlug` null means an owner-authored brief. */
export interface AdBriefDto {
  id: number;
  packSlug: string | null;
  brand: string;
  premise: string | null;
  tone: string | null;
  structure: string | null;
  enabled: boolean;
  createdAt: string;
}

/** `GET /api/ad-briefs` — a bare, unpaged array (T403b's own YAGNI call: briefs are dozens, not
 * thousands — see `AdBriefsController`'s own remarks). No query string, unlike `/api/ads`. */
export const AD_BRIEFS_PATH = "/api/ad-briefs";

export interface AdMutationFailure {
  ok: false;
  status: number;
  detail: string;
  /** The offending field name (e.g. `"script"`), present on a save-time validation 400 only. */
  field?: string;
  /** `AdScriptRuleIds`' own stable token (e.g. `"duration"`) — kept visible alongside the human
   * `detail` for honesty (PLAN T404's own ruling), never hidden behind a client-side rule→message
   * map that could drift from the server's own vocabulary. */
  ruleId?: string;
}

export type AdMutationOutcome = { ok: true; spot: AdSpotDto } | AdMutationFailure;

/** Renders an `AdMutationFailure` as one line — the `detail` (already a complete, human sentence)
 * plus the rule id in parentheses when present, never a second, hand-maintained rule→copy table. */
export function describeAdMutationFailure(failure: AdMutationFailure): string {
  return failure.ruleId !== undefined ? `${failure.detail} (rule: ${failure.ruleId})` : failure.detail;
}

/** The sparse `AdSpotSaveRequest` wire body shared by create and edit (`AdsController.Create`/
 * `.Update`) — every field always present, `null` standing in for "not supplied"/"unchanged" (the
 * same explicit-null convention `MediaPatch` callers already use), never an omitted key. */
export interface AdSpotSaveBody {
  brand: string | null;
  title: string | null;
  brief: string | null;
  script: string | null;
  voicePlan: AdVoicePlanEntry[] | null;
  spotSeconds: number | null;
  bedMediaId: number | null;
}

async function readAdSpotOutcome(response: Response): Promise<AdMutationOutcome> {
  if (response.ok) {
    const spot = (await response.json()) as AdSpotDto;
    return { ok: true, spot };
  }
  const { detail, field, ruleId } = await readProblemDetails(response);
  return { ok: false, status: response.status, detail, field, ruleId };
}

function networkFailure(): AdMutationFailure {
  return { ok: false, status: 0, detail: "Network error — check your connection." };
}

/** `POST /api/ads` (SPEC F162.1, F160.4; STORY-392 AC2) — always creates an `owner` draft. */
export async function createAdSpot(body: AdSpotSaveBody): Promise<AdMutationOutcome> {
  let response: Response;
  try {
    response = await fetch("/api/ads", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
  } catch {
    return networkFailure();
  }
  return readAdSpotOutcome(response);
}

/** `PATCH /api/ads/{id}` (SPEC F162.1; STORY-392 AC2) — legal only against `draft`/`failed`
 * (409 otherwise, surfaced via {@link AdMutationFailure.detail}). `version` is the row's bare xmin,
 * wrapped into the weak `If-Match` header here — callers never build that header themselves. */
export async function updateAdSpot(id: number, version: string, body: AdSpotSaveBody): Promise<AdMutationOutcome> {
  let response: Response;
  try {
    response = await fetch(`/api/ads/${id}`, {
      method: "PATCH",
      credentials: "include",
      headers: { "Content-Type": "application/json", "If-Match": `W/"${version}"` },
      body: JSON.stringify(body),
    });
  } catch {
    return networkFailure();
  }
  return readAdSpotOutcome(response);
}

type AdVerb = "approve" | "retry" | "retire";

async function postAdVerb(id: number, verb: AdVerb, version: string): Promise<AdMutationOutcome> {
  let response: Response;
  try {
    response = await fetch(`/api/ads/${id}/${verb}`, {
      method: "POST",
      credentials: "include",
      headers: { "If-Match": `W/"${version}"` },
    });
  } catch {
    return networkFailure();
  }
  return readAdSpotOutcome(response);
}

/** `POST /api/ads/{id}/approve` (SPEC F159.4) — draft to approved, re-validating the row's current
 * script first (a brief-only draft cannot approve — the server's own gate, not re-implemented
 * here). */
export const approveAdSpot = (id: number, version: string): Promise<AdMutationOutcome> =>
  postAdVerb(id, "approve", version);

/** `POST /api/ads/{id}/retry` — failed to approved, same re-validation gate as approve. */
export const retryAdSpot = (id: number, version: string): Promise<AdMutationOutcome> =>
  postAdVerb(id, "retry", version);

/** `POST /api/ads/{id}/retire` (SPEC F159.2's as-built rider) — ready|draft|approved|failed to
 * retired. */
export const retireAdSpot = (id: number, version: string): Promise<AdMutationOutcome> =>
  postAdVerb(id, "retire", version);

// ── Briefs (T403b) ───────────────────────────────────────────────────────────────────────────

export interface AdBriefMutationFailure {
  ok: false;
  status: number;
  detail: string;
  field?: string;
}

export type AdBriefMutationOutcome = { ok: true; brief: AdBriefDto } | AdBriefMutationFailure;

function networkBriefFailure(): AdBriefMutationFailure {
  return { ok: false, status: 0, detail: "Network error — check your connection." };
}

async function readAdBriefOutcome(response: Response): Promise<AdBriefMutationOutcome> {
  if (response.ok) {
    const brief = (await response.json()) as AdBriefDto;
    return { ok: true, brief };
  }
  const { detail, field } = await readProblemDetails(response);
  return { ok: false, status: response.status, detail, field };
}

export interface AdBriefCreateBody {
  brand: string;
  premise: string | null;
  tone: string | null;
  structure: string | null;
}

/** `POST /api/ad-briefs` (SPEC F162.1's add form) — owner briefs only; 409 on a duplicate brand
 * (surfaced verbatim via {@link AdBriefMutationFailure.detail} — the server's own message, never a
 * second client-side wording of the same rule). */
export async function createAdBrief(body: AdBriefCreateBody): Promise<AdBriefMutationOutcome> {
  let response: Response;
  try {
    response = await fetch("/api/ad-briefs", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
  } catch {
    return networkBriefFailure();
  }
  return readAdBriefOutcome(response);
}

/** `PATCH /api/ad-briefs/{id}` (SPEC F162.1's enable/disable toggle) — flips `enabled` on any
 * brief, pack or owner alike. No `If-Match` ceremony (T403b's own YAGNI ruling — a bool toggle has
 * no lost-update hazard). */
export async function setAdBriefEnabled(id: number, enabled: boolean): Promise<AdBriefMutationOutcome> {
  let response: Response;
  try {
    response = await fetch(`/api/ad-briefs/${id}`, {
      method: "PATCH",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ enabled }),
    });
  } catch {
    return networkBriefFailure();
  }
  return readAdBriefOutcome(response);
}
