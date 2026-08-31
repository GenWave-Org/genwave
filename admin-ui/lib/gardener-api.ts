// Client-side wire shapes + fetchers for the Library Gardener's admin surface (SPEC F153.9,
// F153.10; STORY-374, STORY-376; PLAN T378, gh-#529) — GET /api/gardener/findings, POST
// /api/gardener/findings/{id}/dismiss, plus the two existing mutation endpoints the Gardener page
// reuses (POST /api/media/eligibility narrowed by an explicit id set, POST /api/media/{id}/reenrich)
// rather than minting anything new. This module is the ONE source for every Gardener wire shape
// (mirrors shows-rotation-api.ts's own "T362 review LOW-7" precedent) — lib/broadcast-api.ts's
// `StatusResponse.gardener` field imports {@link GardenerStatusSummary} from here rather than
// redeclaring it. Browser fetches go through the Next.js same-origin rewrite, the same convention
// every other *-api.ts module in this folder follows.

import { readErrorMessage } from "@/lib/problem-details";

/** The five rot-finding kinds (SPEC F153.1), snake_case on the wire — `GenWave.Core.Domain.RotKindTokens`'s
 * own tokens, never re-cased to this station's usual camelCase convention (the evidence JSONB
 * shares this same snake_case posture — see {@link evidenceChips}'s own remarks). */
export type GardenerKind = "dead_file" | "near_duplicate" | "stale_metadata" | "unreachable" | "shelf_dust";

export type GardenerFindingState = "open" | "dismissed" | "resolved";

/** Section order (SPEC F153.10, ORCHESTRATOR ruling 2) — fixed, never derived from whatever order
 * the api response happens to list groups in. */
export const GARDENER_KIND_ORDER: readonly GardenerKind[] = [
  "dead_file",
  "near_duplicate",
  "stale_metadata",
  "unreachable",
  "shelf_dust",
];

/** Section header copy — sentence-cased per Dean's copy rule (capitals open every sentence). */
export const GARDENER_KIND_LABELS: Record<GardenerKind, string> = {
  dead_file: "Dead files",
  near_duplicate: "Near duplicates",
  stale_metadata: "Stale metadata",
  unreachable: "Unreachable",
  shelf_dust: "Shelf dust",
};

/** LOW-2 — per-kind empty state, one line each ("Nothing here." read as generic across every
 * section; naming the kind again reassures an operator the right section loaded, just with
 * nothing to act on). */
export const GARDENER_KIND_EMPTY_LABELS: Record<GardenerKind, string> = {
  dead_file: "No dead files.",
  near_duplicate: "No near duplicates.",
  stale_metadata: "No stale metadata.",
  unreachable: "Nothing unreachable.",
  shelf_dust: "No shelf dust.",
};

/**
 * SMOKE-2 (orchestrator, PLAN T378) — the dashboard tile's per-kind breakdown phrase, count first.
 * `dead_file`/`near_duplicate` genuinely pluralise ("1 dead file" vs "3 dead files"); the other
 * three are phrased so the noun position never needs a singular/plural branch at all — "stale
 * metadata" is treated as uncountable ("1 with stale metadata", not "1 stale metadatas"),
 * "unreachable" and "on the shelf" are adjectival/prepositional and read identically at any count.
 */
export function gardenerCountPhrase(kind: GardenerKind, count: number): string {
  switch (kind) {
    case "dead_file":
      return count === 1 ? "1 dead file" : `${count} dead files`;
    case "near_duplicate":
      return count === 1 ? "1 near duplicate" : `${count} near duplicates`;
    case "stale_metadata":
      return `${count} with stale metadata`;
    case "unreachable":
      return `${count} unreachable`;
    case "shelf_dust":
      return `${count} on the shelf`;
  }
}

/** `GET /api/status`'s own per-kind OPEN totals (SPEC F153.9) — `deadFile`/`nearDuplicate`/
 * `staleMetadata`/`unreachable`/`shelfDust` are camelCase here, unlike {@link GardenerKind}'s wire
 * tokens: this object is ordinary JSON the api's default naming policy serializes, never a
 * hand-built JSONB blob like the finding evidence is. */
export interface GardenerOpenCounts {
  deadFile: number;
  nearDuplicate: number;
  staleMetadata: number;
  unreachable: number;
  shelfDust: number;
}

/** `GET /api/status`'s `gardener` block (SPEC F153.9) — the dashboard tile's own data. */
export interface GardenerStatusSummary {
  open: GardenerOpenCounts;
  total: number;
}

/** Maps a {@link GardenerKind} to its own field on {@link GardenerOpenCounts} — the one place this
 * kind-token ↔ camelCase-field correspondence is written down, so a page section and the tile can't
 * quietly drift onto two different maps. */
export const GARDENER_OPEN_COUNT_KEY: Record<GardenerKind, keyof GardenerOpenCounts> = {
  dead_file: "deadFile",
  near_duplicate: "nearDuplicate",
  stale_metadata: "staleMetadata",
  unreachable: "unreachable",
  shelf_dust: "shelfDust",
};

/** One finding's nested `media` projection (SPEC F153.9) — path/title/artist/durationMs/plays/
 * rating/neverPlay/eligible, sourced entirely from `IRotFindingStore.ListWithMediaAsync`'s one
 * joined read. `title`/`artist`/`rating` are `null`-able: an unenriched or never-voted row carries
 * no value for any of the three. */
export interface GardenerMediaDto {
  path: string;
  title: string | null;
  artist: string | null;
  durationMs: number | null;
  plays: number;
  rating: number | null;
  neverPlay: boolean;
  eligible: boolean;
}

/** One finding row (SPEC F153.9) — `evidence` is a parsed JSON object, never a re-stringified
 * blob; its own shape depends on `kind` (the enclosing group's, not a per-row field) — see
 * {@link evidenceChips}. `mediaId` is a JSON NUMBER on this wire (`RotFinding.MediaId` is a bare
 * `long`, unlike the catalog's own string-typed `AdminMediaDto.MediaId`) — every mutation this
 * module calls (`setEligibilityForMediaIds`, `reenrichMedia`) takes that same numeric id. */
export interface GardenerFindingDto {
  id: number;
  mediaId: number;
  state: GardenerFindingState;
  evidence: unknown;
  openedAt: string;
  resolvedAt: string | null;
  dismissedAt: string | null;
  media: GardenerMediaDto;
}

/** A `near_duplicate` group's own members — the SAME row objects `findings` already lists,
 * re-grouped by `groupKey` (SPEC F153.9). Empty/absent for every other kind. */
export interface GardenerDuplicateGroupDto {
  groupKey: string | null;
  members: GardenerFindingDto[];
}

export interface GardenerGroupDto {
  kind: GardenerKind;
  findings: GardenerFindingDto[];
  duplicateGroups: GardenerDuplicateGroupDto[];
}

export interface GardenerFindingsResponse {
  groups: GardenerGroupDto[];
}

function isGardenerFindingsResponse(raw: unknown): raw is GardenerFindingsResponse {
  return typeof raw === "object" && raw !== null && Array.isArray((raw as { groups?: unknown }).groups);
}

/** `GET /api/gardener/findings?state=open&limit=1000` (ORCHESTRATOR ruling 2 — the page's own
 * "whole queue in one page" read, T377's own ceiling). Never throws: a network failure, non-2xx,
 * or off-shape 200 body all resolve to `null` so the page can render its own unavailable state. */
export async function fetchGardenerFindings(): Promise<GardenerFindingsResponse | null> {
  try {
    const response = await fetch("/api/gardener/findings?state=open&limit=1000", {
      credentials: "include",
      cache: "no-store",
    });
    if (!response.ok) return null;
    const raw = (await response.json()) as unknown;
    return isGardenerFindingsResponse(raw) ? raw : null;
  } catch {
    return null;
  }
}

export type DismissFindingOutcome = { ok: true } | { ok: false; detail: string };

/** `POST /api/gardener/findings/{id}/dismiss` (SPEC F153.2) — 204 on success; every other outcome
 * (404 unknown/already-settled, network failure) resolves to a classified failure the caller
 * toasts, mirroring every other mutation module's never-throw contract. */
export async function dismissGardenerFinding(findingId: number): Promise<DismissFindingOutcome> {
  let response: Response;
  try {
    response = await fetch(`/api/gardener/findings/${findingId}/dismiss`, {
      method: "POST",
      credentials: "include",
    });
  } catch {
    return { ok: false, detail: "Network error — check your connection." };
  }
  if (!response.ok) {
    return { ok: false, detail: await readErrorMessage(response) };
  }
  return { ok: true };
}

export type SetEligibilityOutcome = { ok: true; affected: number } | { ok: false; detail: string };

/**
 * `POST /api/media/eligibility` narrowed to an explicit id set (SPEC F153.10, STORY-376 AC6, PLAN
 * T378) — the ONE write this page uses for BOTH a single row's eligibility toggle (a one-id list)
 * and "Keep this one" (the OTHER duplicate-group members' ids): one call, one failure contract, no
 * second fetcher for what is structurally the same bulk write at two different id-list lengths.
 * All-or-nothing server-side (`MediaController.BulkSetEligibility`) — never N per-row PATCHes that
 * can half-fail.
 */
export async function setEligibilityForMediaIds(
  mediaIds: readonly number[],
  eligible: boolean
): Promise<SetEligibilityOutcome> {
  let response: Response;
  try {
    response = await fetch("/api/media/eligibility", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ eligible, filter: { mediaIds } }),
    });
  } catch {
    return { ok: false, detail: "Network error — check your connection." };
  }
  if (!response.ok) {
    return { ok: false, detail: await readErrorMessage(response) };
  }
  const body = (await response.json()) as { affected?: number };
  return { ok: true, affected: body.affected ?? 0 };
}

export type ReenrichOutcome = { ok: true } | { ok: false; detail: string };

/** `POST /api/media/{id}/reenrich`, `fields` omitted — `ReenrichController`'s own "missing or
 * empty -> all" default (a full re-analysis). The Gardener row's "Re-enrich" verb is a single,
 * one-click action, deliberately never `ReanalyzePanel`'s per-field picker — this queue exists to
 * clear a finding, not to fine-tune which columns get reset. */
export async function reenrichMedia(mediaId: number): Promise<ReenrichOutcome> {
  let response: Response;
  try {
    response = await fetch(`/api/media/${mediaId}/reenrich`, {
      method: "POST",
      credentials: "include",
    });
  } catch {
    return { ok: false, detail: "Network error — check your connection." };
  }
  if (response.status === 202) return { ok: true };
  return { ok: false, detail: await readErrorMessage(response) };
}

// ── File actions (SPEC F154; STORY-379; PLAN T381) ──────────────────────────────────────────────

export type GardenerFileActionVerb = "retag" | "rename" | "move";

/** One tag field a retag would write (SPEC F154.5) — `fileValue` is the file's own CURRENT value
 * (`null` when the tag is absent), `catalogValue` is what will be written. */
export interface FileActionTagDiffEntry {
  field: string;
  fileValue: string | null;
  catalogValue: string;
}

/** `POST /api/gardener/file-actions/dry-run`'s own 200 body (SPEC F154.5) — `from`/`to` are real
 * paths (this endpoint is AdminOnly; the operator must see what they are about to do before
 * confirming it). */
export interface FileActionPlanDto {
  from: string;
  to: string;
  tagDiff: FileActionTagDiffEntry[];
  planToken: string;
  expiresAt: string;
}

function isFileActionPlanDto(raw: unknown): raw is FileActionPlanDto {
  if (typeof raw !== "object" || raw === null) return false;
  const candidate = raw as { from?: unknown; to?: unknown; planToken?: unknown; tagDiff?: unknown };
  return (
    typeof candidate.from === "string" &&
    typeof candidate.to === "string" &&
    typeof candidate.planToken === "string" &&
    Array.isArray(candidate.tagDiff)
  );
}

export type FileActionDryRunOutcome =
  | { ok: true; plan: FileActionPlanDto }
  | { ok: false; status: number; detail: string };

/**
 * `POST /api/gardener/file-actions/dry-run` (SPEC F154.1-F154.3, F154.5; STORY-379; PLAN T381) —
 * plans one of the three file actions (retag/rename/move) and mints the plan token `confirmFileAction`
 * presents back. `status` rides along on failure so the dialog can special-case a 404 (file actions
 * disabled, SPEC F154.2) from an ordinary refusal (400/409) without a second parse.
 */
export async function dryRunFileAction(
  mediaId: number,
  verb: GardenerFileActionVerb,
  target: string | null
): Promise<FileActionDryRunOutcome> {
  let response: Response;
  try {
    response = await fetch("/api/gardener/file-actions/dry-run", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ mediaId, verb, target }),
    });
  } catch {
    return { ok: false, status: 0, detail: "Network error — check your connection." };
  }
  if (!response.ok) {
    return { ok: false, status: response.status, detail: await readErrorMessage(response) };
  }
  const raw = (await response.json()) as unknown;
  if (!isFileActionPlanDto(raw)) {
    return { ok: false, status: response.status, detail: "Unexpected response shape." };
  }
  return { ok: true, plan: raw };
}

export type FileActionConfirmOutcome =
  | { kind: "done"; to: string }
  | { kind: "conflict" | "reverted" | "busy" }
  | { kind: "refused"; rule: string; message: string }
  | { kind: "error"; detail: string };

interface ConfirmOutcomeBody {
  outcome: string;
  to?: string;
  rule?: string;
  message?: string;
}

function isConfirmOutcomeBody(raw: unknown): raw is ConfirmOutcomeBody {
  return typeof raw === "object" && raw !== null && typeof (raw as { outcome?: unknown }).outcome === "string";
}

/**
 * `POST /api/gardener/file-actions/confirm` (SPEC F154.4-F154.8; STORY-379; PLAN T381) — presents a
 * dry-run's own plan token back. Every status this endpoint can return (200/400/409/500/503) may
 * carry EITHER the `{ outcome }` wire shape (done/conflict/reverted/refused/busy — the controller's
 * own status map) or a plain ProblemDetails failure (a missing/expired token, or the generic 500) —
 * this reads the body once and branches on its ACTUAL shape rather than assuming one from the
 * status code alone, since a 409 carries either shape depending on why.
 */
export async function confirmFileAction(planToken: string): Promise<FileActionConfirmOutcome> {
  let response: Response;
  try {
    response = await fetch("/api/gardener/file-actions/confirm", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ planToken }),
    });
  } catch {
    return { kind: "error", detail: "Network error — check your connection." };
  }

  const raw = (await response.json().catch(() => null)) as unknown;

  if (isConfirmOutcomeBody(raw)) {
    switch (raw.outcome) {
      case "done":
        return { kind: "done", to: raw.to ?? "" };
      case "conflict":
        return { kind: "conflict" };
      case "reverted":
        return { kind: "reverted" };
      case "busy":
        return { kind: "busy" };
      case "refused":
        return { kind: "refused", rule: raw.rule ?? "", message: raw.message ?? "The action was refused." };
      default:
        break;
    }
  }

  const detail =
    typeof raw === "object" && raw !== null && typeof (raw as { detail?: unknown }).detail === "string"
      ? (raw as { detail: string }).detail
      : `Unexpected error (${response.status}).`;
  return { kind: "error", detail };
}

// ── Evidence chips ──────────────────────────────────────────────────────────────────────────────

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function pluralize(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

/**
 * Compact evidence chips for one row, per kind (SPEC F153.10's "render it" requirement — the
 * contract's own per-kind evidence shapes). Every evidence JSONB is written directly by
 * `Garden.RotFindingRepository`'s SQL (`jsonb_build_object`) — snake_case field names throughout,
 * NEVER re-cased through this station's usual camelCase wire convention (`GardenerController`'s own
 * remarks: `evidence` is deserialized and forwarded as-is, never re-serialized). Defensive against a
 * malformed or future-shaped blob: a missing/mistyped field is silently skipped rather than
 * throwing mid-render — this is a read-only display, never a validated write boundary.
 */
export function evidenceChips(kind: GardenerKind, evidence: unknown): string[] {
  if (!isRecord(evidence)) return [];

  switch (kind) {
    case "dead_file": {
      const chips: string[] = [];
      if (typeof evidence["reason"] === "string") chips.push(`Reason: ${evidence["reason"]}`);
      if (typeof evidence["since"] === "string") chips.push(`Since ${evidence["since"].slice(0, 10)}`);
      return chips;
    }
    case "near_duplicate": {
      const chips: string[] = [];
      if (typeof evidence["title_variant"] === "string" && evidence["title_variant"] !== "") {
        chips.push(`Variant: ${evidence["title_variant"]}`);
      }
      const siblings = evidence["siblings"];
      if (Array.isArray(siblings)) chips.push(pluralize(siblings.length, "sibling"));
      const versions = evidence["versions"];
      if (Array.isArray(versions) && versions.length > 0) {
        chips.push(`${pluralize(versions.length, "other version")}`);
      }
      return chips;
    }
    case "stale_metadata": {
      const fields = evidence["fields"];
      if (!Array.isArray(fields)) return [];
      return fields.filter((field): field is string => typeof field === "string").map((field) => `Missing: ${field}`);
    }
    case "shelf_dust": {
      const chips: string[] = [];
      const days = evidence["days_on_shelf"];
      if (typeof days === "number") chips.push(`${pluralize(days, "day")} on the shelf`);
      return chips;
    }
    case "unreachable": {
      const chips: string[] = [];
      if (typeof evidence["reason"] === "string") chips.push(`Reason: ${evidence["reason"]}`);
      const envelopes = evidence["envelopes"];
      if (typeof envelopes === "number") chips.push(`${pluralize(envelopes, "envelope")} checked`);
      return chips;
    }
  }
}
