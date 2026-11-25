// Wire shapes + narrowing for the Talk rules routes (SPEC F220.26, F220.27; STORY-572; PLAN T804):
// `GET /api/talk-rules`, `PUT /api/talk-rules/{id}/{kind}`, `GET /api/talk-rules/{id}/flags` and
// `POST /api/talk-rules/replay` (Host `TalkRulesController`). This module owns only shape and
// narrowing — the page fetches through `apiFetch` / `apiGet` like every other admin page.

/** The three kinds of talk a rule can cover, as the Host's `TalkKindSlugs` spells them. */
export const TALK_KINDS = ["dj-line", "chat", "ad"] as const;
export type TalkKind = (typeof TALK_KINDS)[number];

/** Plain-words labels (no radio jargon). */
export const TALK_KIND_LABELS: Readonly<Record<TalkKind, string>> = {
  "dj-line": "DJ lines",
  chat: "Chats",
  ad: "Ads",
};

/** Plain-words labels for the Host's outcome tokens (`TalkOutcome`, kebab-cased). */
export const OUTCOME_LABELS: Readonly<Record<string, string>> = {
  accepted: "Used",
  "re-asked": "Asked again",
  template: "Used a ready-made line",
  dropped: "Dropped",
  failed: "Failed",
};

/** The label for an outcome token; an unknown token is shown as it came. */
export function outcomeLabel(token: string): string {
  return OUTCOME_LABELS[token] ?? token;
}

/** The label for a kind token from the wire; an unknown token is shown as it came. */
export function kindLabel(token: string): string {
  return isTalkKind(token) ? TALK_KIND_LABELS[token] : token;
}

/** `TalkRules:Mode`'s settings key (SPEC F220.18). */
export const TALK_RULES_MODE_KEY = "TalkRules:Mode";

export type TalkRulesMode = "legacy" | "shadow" | "rules";

export interface TalkRuleDto {
  id: string;
  name: string;
  whatItDoes: string;
  why: string;
  example: string;
  canCover: TalkKind[];
  switches: Partial<Record<TalkKind, boolean>>;
  flags7d: Partial<Record<TalkKind, number>>;
}

export interface TalkFlaggedAttemptDto {
  checkId: number;
  when: string;
  dj: string | null;
  kind: string;
  words: string;
  outcome: string;
}

export interface TalkRuleCountDto {
  ruleId: string;
  checked: number;
  flagged: number;
}

export interface TalkRulesReplayDto {
  counts: TalkRuleCountDto[];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

export function isTalkKind(value: unknown): value is TalkKind {
  return TALK_KINDS.some((kind) => kind === value);
}

function narrowKindMap<T>(raw: unknown, isValue: (v: unknown) => v is T): Partial<Record<TalkKind, T>> {
  const out: Partial<Record<TalkKind, T>> = {};
  if (!isRecord(raw)) return out;
  for (const kind of TALK_KINDS) {
    const value = raw[kind];
    if (isValue(value)) out[kind] = value;
  }
  return out;
}

const isBoolean = (v: unknown): v is boolean => typeof v === "boolean";
const isNumber = (v: unknown): v is number => typeof v === "number";

function narrowRule(raw: unknown): TalkRuleDto | null {
  if (!isRecord(raw)) return null;
  const { id, name, whatItDoes, why, example, canCover } = raw;
  if (typeof id !== "string" || typeof name !== "string" || typeof whatItDoes !== "string") return null;
  if (!Array.isArray(canCover)) return null;
  return {
    id,
    name,
    whatItDoes,
    why: typeof why === "string" ? why : "",
    example: typeof example === "string" ? example : "",
    canCover: canCover.filter(isTalkKind),
    switches: narrowKindMap(raw["switches"], isBoolean),
    flags7d: narrowKindMap(raw["flags7d"], isNumber),
  };
}

/** Narrows `GET /api/talk-rules`; `null` when the body is not a list (the page shows its load error). */
export function narrowTalkRules(raw: unknown): TalkRuleDto[] | null {
  if (!Array.isArray(raw)) return null;
  const rules: TalkRuleDto[] = [];
  for (const entry of raw) {
    const rule = narrowRule(entry);
    if (rule === null) return null;
    rules.push(rule);
  }
  return rules;
}

/** Narrows `GET /api/talk-rules/{id}/flags`; entries that don't fit are dropped. */
export function narrowFlaggedAttempts(raw: unknown): TalkFlaggedAttemptDto[] {
  if (!Array.isArray(raw)) return [];
  const out: TalkFlaggedAttemptDto[] = [];
  for (const entry of raw) {
    if (!isRecord(entry)) continue;
    const { checkId, when, dj, kind, words, outcome } = entry;
    if (typeof checkId !== "number" || typeof when !== "string" || typeof kind !== "string") continue;
    if (typeof words !== "string" || typeof outcome !== "string") continue;
    out.push({ checkId, when, dj: typeof dj === "string" ? dj : null, kind, words, outcome });
  }
  return out;
}

/** Narrows the replay response to its per-rule counts; `null` when malformed. */
export function narrowReplay(raw: unknown): TalkRulesReplayDto | null {
  if (!isRecord(raw) || !Array.isArray(raw["counts"])) return null;
  const counts: TalkRuleCountDto[] = [];
  for (const entry of raw["counts"]) {
    if (!isRecord(entry)) return null;
    const { ruleId, checked, flagged } = entry;
    if (typeof ruleId !== "string" || typeof checked !== "number" || typeof flagged !== "number") return null;
    counts.push({ ruleId, checked, flagged });
  }
  return { counts };
}

/** The `TalkRules:Mode` value from a `GET /api/settings` body; `null` when absent or unreadable. */
export function talkRulesModeFrom(settings: unknown): TalkRulesMode | null {
  if (!Array.isArray(settings)) return null;
  for (const row of settings) {
    if (!isRecord(row) || row["key"] !== TALK_RULES_MODE_KEY) continue;
    const value = typeof row["value"] === "string" ? row["value"].trim().toLowerCase() : "";
    if (value === "legacy" || value === "shadow" || value === "rules") return value;
  }
  return null;
}

/** True when today's checks still decide what airs (SPEC F220.19). Unknown mode counts as yes — never
 * claim the rules decide without proof. */
export function todaysChecksStillDecide(mode: TalkRulesMode | null): boolean {
  return mode !== "rules";
}
