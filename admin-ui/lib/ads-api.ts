// Client-side wire shapes + fetchers for the Ads admin surface (SPEC F162.1, F174.1; STORY-392;
// PLAN T404, T448) — GET /api/ads (paged, state-scoped), GET /api/ads/{id}, POST /api/ads, PATCH
// /api/ads/{id}, POST /api/ads/{id}/approve|retry|retire|write|preview, DELETE /api/ads/{id}/job,
// GET/POST /api/ad-briefs, PATCH /api/ad-briefs/{id}, GET /api/media?imagingKind=jingle&jingleRole=bed,
// POST /api/sponsors. Mirrors gardener-api.ts's own convention (the ONE source for every Ads wire
// shape) rather than widening broadcast-api.ts, which is scoped to live-broadcast state
// (now-playing/status/ratings) — a separate feature-shaped module keeps the two from coupling on an
// unrelated read (the shows-rotation-api.ts/gardener-api.ts precedent this folder already holds for
// every other feature). Browser fetches go through the Next.js same-origin rewrite, same as every
// other *-api.ts module here; the page's own server-rendered GETs (`apiGet`, forwarding the request
// cookie) build their paths from the exported `build*Path` helpers below rather than calling fetch
// directly, mirroring `buildGardenerFindingsPath`. The SpotWizard (PLAN T448) is this module's only
// caller for `write`/`preview`/`cancelSpotJob`/`listBackgroundMusic`/`createSponsor` — every other
// fetcher here predates it.

import { readProblemDetails } from "@/lib/problem-details";
import { SPONSORS_PATH, type SponsorDto, type SponsorRefDto } from "@/lib/sponsors-api";

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

/** The job's own `"write"`/`"preview"` token (`GenWave.Host.Api.AdSpotJobDto.Kind`) — declared here,
 * above {@link AdSpotJobDto}, since a job's presence alone doesn't mean one is running (see
 * {@link inFlightJob}). */
export type AdJobKind = "write" | "preview";

/** `AdSpotJobDto`'s exact wire shape (`GenWave.Host.Api.AdSpotJobDto`, PLAN T441, T448) — the row's
 * own write/preview job. `kind: null` with `error` set means the last job failed and none is
 * running; `error` is a ProblemDetails-style human sentence, already complete. */
export interface AdSpotJobDto {
  kind: AdJobKind | null;
  startedAt: string | null;
  waitingForStation: boolean;
  error: string | null;
}

/** Whether `job` is a job actually IN FLIGHT — every caller must branch on this, never on
 * `job !== null` alone: the row keeps `job` non-null with `kind: null` after a failure (the error
 * stays visible until the next job starts), so `kind !== null` is the only correct "one is
 * running" test. Returns `job` itself once it is genuinely running, else `null`. */
export function inFlightJob(job: AdSpotJobDto | null): AdSpotJobDto | null {
  return job !== null && job.kind !== null ? job : null;
}

/** `AdSpotPreviewDto`'s exact wire shape (`GenWave.Host.Api.AdSpotPreviewDto`, PLAN T442, T448) —
 * present once a preview has rendered at least once; `stale` is the server's own recomputed
 * `preview_key` comparison (SPEC F174.4), never derived client-side. */
export interface AdSpotPreviewDto {
  at: string;
  key: string;
  stale: boolean;
}

/** `AdSpotDto`'s exact wire shape (`GenWave.Host.Api.AdSpotDto`, SPEC F162.1, F171.7, F174.1;
 * STORY-392, STORY-412, STORY-421; PLAN T403, T436, T447, T448). `version` is the bare xmin token —
 * the same `If-Match: W/"<version>"` convention `lib/use-row-patch.ts` already holds for
 * `/api/media/{id}`, applied here for every Ads verb. `sponsorName` is the created-at/refreshed-on-
 * change snapshot the wire already carries; `sponsor` is the live {@link SponsorRefDto} cross-
 * reference — a spot references its sponsor by id/name/paused, never a free-text customer label.
 * `job`/`preview` are the SpotWizard's own Script/Hear-step state (PLAN T448's wire-gap fix — these
 * two members were missing here although the C# record has always carried them). */
export interface AdSpotDto {
  id: number;
  sponsorId: number;
  sponsorName: string;
  sponsor: SponsorRefDto;
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
  job: AdSpotJobDto | null;
  preview: AdSpotPreviewDto | null;
}

/** `GET /api/ads`'s own `{ items, total }` envelope (`AdsController.List`, the
 * `GardenerController.GetFindings` paging idiom). */
export interface AdsListResponse {
  items: AdSpotDto[];
  total: number;
}

/** `GET /api/ads?state=&sponsorId=&limit=&offset=` (SPEC F162.1, F171.7) — always state-scoped
 * (the page never lists "any state" — each tab is exactly one). `sponsorId` narrows to that one
 * sponsor's spots (SPEC F171.8's own rail selection); `null` omits the filter entirely ("All
 * sponsors"). Mirrors `buildGardenerFindingsPath`. */
export function buildAdsListPath(state: AdState, limit: number, offset: number, sponsorId: number | null): string {
  const query = new URLSearchParams();
  query.set("state", state);
  query.set("limit", String(limit));
  query.set("offset", String(offset));
  if (sponsorId !== null) query.set("sponsorId", String(sponsorId));
  return `/api/ads?${query.toString()}`;
}

/** `AdBriefDto`'s exact wire shape (`GenWave.Host.Api.AdBriefDto`, SPEC F162.1/F162.2, F171.6;
 * PLAN T403b, T435, T447). `packSlug` null means an owner-authored brief. `sponsor` is the brief's
 * own {@link SponsorRefDto} cross-reference — there is no top-level free-text company-name field
 * anywhere on this shape; a brief's sponsor IS its `sponsor.name`. */
export interface AdBriefDto {
  id: number;
  packSlug: string | null;
  sponsor: SponsorRefDto;
  premise: string | null;
  tone: string | null;
  structure: string | null;
  enabled: boolean;
  createdAt: string;
}

/** `GET /api/ad-briefs` — a bare, unpaged array (T403b's own YAGNI call: briefs are dozens, not
 * thousands — see `AdBriefsController`'s own remarks). No query string, unlike `/api/ads`. */
export const AD_BRIEFS_PATH = "/api/ad-briefs";

/** The shared shape of a failed fetch below `{ok:false, status, detail}` — every outcome union in
 * this module that carries no extra field beyond that (briefs, job-cancel, sponsor create) fails
 * onto this one record rather than a near-identical interface of its own. A caller needing an
 * extra field beyond these three (e.g. {@link AdMutationFailure}'s `ruleId`) declares its own
 * interface instead of extending this one — no failure shape here needs a cast to satisfy another. */
export interface ApiFailure {
  ok: false;
  status: number;
  detail: string;
}

export type AdBriefListOutcome = { ok: true; briefs: AdBriefDto[] } | ApiFailure;

/** `GET /api/ad-briefs`, browser-side (PLAN T448) — the Angle & length step's own read, scoped to
 * one sponsor's enabled briefs by the caller. `page.tsx`'s Briefs tab reads the same path
 * server-side via `apiGet` instead (T447's own precedent) — this is the client-fetch twin, needed
 * here because the SpotWizard opens as a modal with no server-rendered props to carry the list. */
export async function listAdBriefs(): Promise<AdBriefListOutcome> {
  let response: Response;
  try {
    response = await fetch(AD_BRIEFS_PATH, { credentials: "include" });
  } catch {
    return networkFailure();
  }
  if (response.ok) {
    const briefs = (await response.json()) as AdBriefDto[];
    return { ok: true, briefs };
  }
  const { detail } = await readProblemDetails(response);
  return { ok: false, status: response.status, detail };
}

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
  /** ProblemDetails `type` URI token (e.g. `"preview_stale"`, `"sponsor_paused"`) — lets a caller
   * branch on a specific failure cause without parsing `detail`'s human text (PLAN T448's own
   * need: the Approve step re-fetches the row on `preview_stale` and nothing else). */
  type?: string;
}

export type AdMutationOutcome = { ok: true; spot: AdSpotDto } | AdMutationFailure;

/** Renders an `AdMutationFailure` as one line — the `detail` (already a complete, human sentence)
 * plus the rule id in parentheses when present, never a second, hand-maintained rule→copy table. */
export function describeAdMutationFailure(failure: AdMutationFailure): string {
  return failure.ruleId !== undefined ? `${failure.detail} (rule: ${failure.ruleId})` : failure.detail;
}

/** The sparse `AdSpotSaveRequest` wire body shared by create and edit (`AdsController.Create`/
 * `.Update`) — every field always present, `null` standing in for "not supplied"/"unchanged" (the
 * same explicit-null convention `MediaPatch` callers already use), never an omitted key.
 * `sponsorId` is required on POST (400 `sponsor_required` when null) and optional on PATCH, where
 * `null` means "leave unchanged" — never a free-text customer label. */
export interface AdSpotSaveBody {
  sponsorId: number | null;
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
  const { detail, field, ruleId, type } = await readProblemDetails(response);
  return { ok: false, status: response.status, detail, field, ruleId, type };
}

function networkFailure(): AdMutationFailure {
  return { ok: false, status: 0, detail: "Network error — check your connection." };
}

/** `GET /api/ads/{id}` (SPEC F174.1; PLAN T448) — refetches one row fresh: the SpotWizard's own
 * 2 s job/preview poll, and its Approve step's re-fetch after a 409 `preview_stale`. */
export async function fetchAdSpot(id: number): Promise<AdMutationOutcome> {
  let response: Response;
  try {
    response = await fetch(`/api/ads/${id}`, { credentials: "include" });
  } catch {
    return networkFailure();
  }
  return readAdSpotOutcome(response);
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

// ── Write/preview jobs (T441, T442; PLAN T448) ──────────────────────────────────────────────────

/** Neither job endpoint takes `If-Match` (`AdsController.Write`/`.Preview` read the row fresh and
 * gate on its current STATE, not a caller-supplied version) — unlike every verb above. */
async function postAdJob(id: number, kind: AdJobKind): Promise<AdMutationOutcome> {
  let response: Response;
  try {
    response = await fetch(`/api/ads/${id}/${kind}`, { method: "POST", credentials: "include" });
  } catch {
    return networkFailure();
  }
  return readAdSpotOutcome(response);
}

/** `POST /api/ads/{id}/write` (SPEC F174.3; PLAN T441) — enqueues the station's own "write it for
 * me" job; 202 carries the fresh row with `job` now set. 409 `ad_write_not_draft`/`ad_job_busy`,
 * 429 `ad_job_queue_full`. */
export const writeSpot = (id: number): Promise<AdMutationOutcome> => postAdJob(id, "write");

/** `POST /api/ads/{id}/preview` (SPEC F174.4; PLAN T442) — enqueues a preview render; 202 carries
 * the fresh row with `job` now set. 400 `script_required`, 409 `ad_preview_not_editable`/
 * `ad_job_busy`, 429 `ad_job_queue_full`. */
export const previewSpot = (id: number): Promise<AdMutationOutcome> => postAdJob(id, "preview");

export type AdJobCancelOutcome = { ok: true } | ApiFailure;

/** `DELETE /api/ads/{id}/job` (SPEC F174.3; PLAN T441, T448) — cancels whatever job is running or
 * queued for this spot; 204 with no body either way (`AdsController.DeleteJob`'s own idempotent
 * posture, even with no active job to cancel), so this returns no spot — the caller re-fetches via
 * {@link fetchAdSpot} to see the row's `job` cleared. */
export async function cancelSpotJob(id: number): Promise<AdJobCancelOutcome> {
  let response: Response;
  try {
    response = await fetch(`/api/ads/${id}/job`, { method: "DELETE", credentials: "include" });
  } catch {
    return networkFailure();
  }
  if (response.ok) return { ok: true };
  const { detail } = await readProblemDetails(response);
  return { ok: false, status: response.status, detail };
}

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
  sponsorId: number;
  premise: string | null;
  tone: string | null;
  structure: string | null;
}

/** `POST /api/ad-briefs` (SPEC F162.1's add form, F171.6's sponsor-first cap) — owner briefs only;
 * 409 on a duplicate (sponsor, premise) pair (surfaced verbatim via
 * {@link AdBriefMutationFailure.detail} — the server's own message, never a second client-side
 * wording of the same rule). */
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

// ── Background music (T446; PLAN T448) ──────────────────────────────────────────────────────────

/** One installed background-music choice for the SpotWizard's Hear step (SPEC F174.7). `pack` is
 * the installing jingle pack's display name, or `null` for a row tagged `jingleRole=bed` outside
 * any pack. */
export interface BackgroundMusicOption {
  mediaId: number;
  title: string;
  pack: string | null;
}

/** The subset of `GenWave.Core.Domain.AdminMediaDto` this reads — `mediaId` arrives as a string on
 * the wire (the same shape `BedPicker`'s own `MediaSearchRow` reads), converted to a number below. */
interface AdminMediaRow {
  mediaId: string;
  title: string | null;
  pack: string | null;
}

/** `GET /api/media?imagingKind=jingle&jingleRole=bed` (T446, SPEC F174.7) — every installed
 * background-music track, for the Hear step's `<select>`. `null` (never an empty array) on any
 * failure, so the step can fall back to offering only its "Let the station pick" default rather
 * than reading an empty list as "nothing installed". `limit=200` matches `MediaController.List`'s
 * own upper clamp — this read never pages. */
export async function listBackgroundMusic(): Promise<BackgroundMusicOption[] | null> {
  let response: Response;
  try {
    response = await fetch("/api/media?imagingKind=jingle&jingleRole=bed&limit=200", { credentials: "include" });
  } catch {
    return null;
  }
  if (!response.ok) return null;
  const rows = (await response.json()) as AdminMediaRow[];
  return rows.map((row) => ({ mediaId: Number(row.mediaId), title: row.title ?? `#${row.mediaId}`, pack: row.pack }));
}

// ── Sponsors (SPEC F171.3; PLAN T448) ───────────────────────────────────────────────────────────

export interface SponsorMutationFailure extends ApiFailure {
  /** ProblemDetails `type` — `"sponsor_name_taken"` on a 409, so the Sponsor step can fold-match
   * against its already-fetched sponsor list without a second round trip. */
  type?: string;
}

export type SponsorMutationOutcome = { ok: true; sponsor: SponsorDto } | SponsorMutationFailure;

/** `POST /api/sponsors {name}` (SPEC F171.3; PLAN T448) — the Sponsor step's inline "Create" path.
 * `SponsorsController.Create` always makes an owner sponsor (no `packSlug`), so this never sends
 * one. */
export async function createSponsor(name: string): Promise<SponsorMutationOutcome> {
  let response: Response;
  try {
    response = await fetch(SPONSORS_PATH, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name }),
    });
  } catch {
    return networkFailure();
  }
  if (response.ok) {
    const sponsor = (await response.json()) as SponsorDto;
    return { ok: true, sponsor };
  }
  const { detail, type } = await readProblemDetails(response);
  return { ok: false, status: response.status, detail, type };
}
