/**
 * Client-side, NON-authoritative read of a portable persona card's FULL text (SPEC F71.1, F79.2,
 * F90.6; ARCHITECTURE.md "Trust ruling") — feeds `PersonaCardReviewModal`, the trust amendment's
 * required stop before ANY import (catalog now, file upload in STORY-236). Unlike
 * `personas/persona-card.ts`'s `parsePersonaCardPreview` (a handful of summary fields for an
 * inline preview), this projects EVERY section the ruling requires the operator to see: soul,
 * quirks, voice, energy, corrections, lore, taste.
 *
 * The server (`PersonaCardSerializer.Deserialize` + `PersonaController.Import`) remains the only
 * validator — a card that fails to parse here still gets a real import attempt against the raw
 * bytes this module never touches, and the modal never sends what it parses, only the original
 * `cardText` byte-for-byte. This only ever degrades to `null` (an unparsable card, or one missing
 * a usable `name`), never throws — every other field tolerates a missing/wrong-typed value by
 * falling back to an empty/zero default (SPEC F79.2's forward-compat: unknown or malformed fields
 * within the current major never crash the read).
 */

export interface PersonaCardReviewVoice {
  engine: string;
  voiceId: string;
}

export interface PersonaCardReviewCorrection {
  from: string;
  to: string;
}

export interface PersonaCardReviewTastePredicate {
  artist: string | null;
  genre: string | null;
  tag: string | null;
}

export interface PersonaCardReviewTasteContext {
  /** `System.DayOfWeek`'s own wire encoding (0 = Sunday … 6 = Saturday) — same convention
   * `lib/persona-taste-inspector-api.ts` documents for the server-computed taste inspector. */
  daysOfWeek: number[];
  startHour: number | null;
  endHour: number | null;
}

export interface PersonaCardReviewTasteRule {
  predicate: PersonaCardReviewTastePredicate;
  context: PersonaCardReviewTasteContext;
  weight: number;
}

export interface PersonaCardReview {
  name: string;
  tagline: string;
  soul: string;
  quirks: string[];
  voice: PersonaCardReviewVoice;
  energyDisposition: number;
  corrections: PersonaCardReviewCorrection[];
  lore: string[];
  taste: PersonaCardReviewTasteRule[];
  /** Every top-level card key this projection above does NOT already read and display, keyed to
   * its raw (still-`unknown`) value (review finding #6) — closes the gap between "what this
   * review shows" and "what confirm actually POSTs" permanently: a newer/forward-compat field
   * (SPEC F79.2) is real bytes the operator is about to adopt, so it must be visible here too,
   * not silently swallowed by this projection's narrower named fields. Empty object when the
   * card carries nothing beyond what's already shown above. */
  otherFields: Record<string, unknown>;
}

interface RawVoice {
  engine?: unknown;
  voiceId?: unknown;
}

interface RawCorrection {
  from?: unknown;
  to?: unknown;
}

interface RawTastePredicate {
  artist?: unknown;
  genre?: unknown;
  tag?: unknown;
}

interface RawTasteContext {
  daysOfWeek?: unknown;
  startHour?: unknown;
  endHour?: unknown;
}

interface RawTasteRule {
  predicate?: unknown;
  context?: unknown;
  weight?: unknown;
}

interface RawPersonaCard {
  name?: unknown;
  tagline?: unknown;
  soul?: unknown;
  quirks?: unknown;
  voice?: unknown;
  energyDisposition?: unknown;
  corrections?: unknown;
  lore?: unknown;
  taste?: unknown;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function asString(value: unknown, fallback = ""): string {
  return typeof value === "string" ? value : fallback;
}

function asNullableString(value: unknown): string | null {
  return typeof value === "string" ? value : null;
}

function asFiniteNumber(value: unknown, fallback = 0): number {
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

function asNullableFiniteNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function asStringArray(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  return value.filter((item): item is string => typeof item === "string");
}

function asNumberArray(value: unknown): number[] {
  if (!Array.isArray(value)) return [];
  return value.filter((item): item is number => typeof item === "number" && Number.isFinite(item));
}

function parseVoice(raw: unknown): PersonaCardReviewVoice {
  if (!isRecord(raw)) return { engine: "", voiceId: "" };
  const v = raw as RawVoice;
  return { engine: asString(v.engine), voiceId: asString(v.voiceId) };
}

function parseCorrections(raw: unknown): PersonaCardReviewCorrection[] {
  if (!Array.isArray(raw)) return [];
  return raw
    .filter((item): item is RawCorrection => isRecord(item))
    .map((item) => ({ from: asString(item.from), to: asString(item.to) }));
}

function parseTastePredicate(raw: unknown): PersonaCardReviewTastePredicate {
  if (!isRecord(raw)) return { artist: null, genre: null, tag: null };
  const p = raw as RawTastePredicate;
  return { artist: asNullableString(p.artist), genre: asNullableString(p.genre), tag: asNullableString(p.tag) };
}

function parseTasteContext(raw: unknown): PersonaCardReviewTasteContext {
  if (!isRecord(raw)) return { daysOfWeek: [], startHour: null, endHour: null };
  const c = raw as RawTasteContext;
  return {
    daysOfWeek: asNumberArray(c.daysOfWeek),
    startHour: asNullableFiniteNumber(c.startHour),
    endHour: asNullableFiniteNumber(c.endHour),
  };
}

function parseTasteRules(raw: unknown): PersonaCardReviewTasteRule[] {
  if (!Array.isArray(raw)) return [];
  return raw
    .filter((item): item is RawTasteRule => isRecord(item))
    .map((item) => ({
      predicate: parseTastePredicate(item.predicate),
      context: parseTasteContext(item.context),
      weight: asFiniteNumber(item.weight),
    }));
}

/** Every named field `PersonaCardReview` reads by hand above — anything else at the card's top
 * level is a key this projection has never heard of (SPEC F79.2 forward-compat), collected into
 * `otherFields` instead of being dropped on the floor. Deliberately does NOT include
 * `schemaVersion`: it isn't rendered as itself anywhere in the review either, so by the same
 * "shown vs posted" rule it belongs in `otherFields` too — full transparency, not a curated list
 * of what an author "should" have used. */
const CONSUMED_CARD_KEYS = new Set([
  "name",
  "tagline",
  "soul",
  "quirks",
  "voice",
  "energyDisposition",
  "corrections",
  "lore",
  "taste",
]);

/**
 * `Object.create(null)` — NOT `{}` — is load-bearing here (review follow-up #1): `JSON.parse`
 * correctly creates `__proto__` as a genuine own property when a payload contains that key (it
 * uses `CreateDataProperty`, not assignment), so a hostile/malformed card's top-level `__proto__`
 * field really is sitting in `raw`. But a plain `{}` accumulator inherits `Object.prototype`'s
 * `__proto__` ACCESSOR — `other["__proto__"] = raw["__proto__"]` against a normal object silently
 * reassigns the accumulator's own prototype instead of creating a "__proto__" entry, so the field
 * would vanish from `Object.entries()` with no error and no display: exactly the shown-vs-posted
 * hole this whole section exists to close. A null-prototype accumulator has no such accessor, so
 * the assignment behaves like an ordinary key on every field name, no exceptions.
 */
function extractOtherFields(raw: Record<string, unknown>): Record<string, unknown> {
  const other: Record<string, unknown> = Object.create(null) as Record<string, unknown>;
  for (const key of Object.keys(raw)) {
    if (!CONSUMED_CARD_KEYS.has(key)) other[key] = raw[key];
  }
  return other;
}

/**
 * Parses `json` into the full review projection. Returns `null` for anything that isn't valid
 * JSON, isn't an object, or is missing a usable `name` — the one field load-bearing enough to
 * fail the whole review on (nothing to show the operator without it). Every other field degrades
 * to an empty/zero default rather than failing the parse.
 */
export function parsePersonaCardReview(json: string): PersonaCardReview | null {
  let raw: unknown;
  try {
    raw = JSON.parse(json);
  } catch {
    return null;
  }

  if (!isRecord(raw)) return null;
  const card = raw as RawPersonaCard;
  if (typeof card.name !== "string" || card.name.trim() === "") return null;

  return {
    name: card.name,
    tagline: asString(card.tagline),
    soul: asString(card.soul),
    quirks: asStringArray(card.quirks),
    voice: parseVoice(card.voice),
    energyDisposition: asFiniteNumber(card.energyDisposition),
    corrections: parseCorrections(card.corrections),
    lore: asStringArray(card.lore),
    taste: parseTasteRules(card.taste),
    otherFields: extractOtherFields(raw),
  };
}

// ---------------------------------------------------------------------------
// Readable-form formatters (review finding #9) — pure functions over the shapes above, co-located
// with the parser they format rather than living in the modal that merely calls them.
// ---------------------------------------------------------------------------

/** `System.DayOfWeek`'s own wire encoding (0 = Sunday … 6 = Saturday) — same convention
 * `lib/persona-taste-inspector-api.ts` documents for the server-computed taste inspector. */
export const DAY_LABELS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

/** Artist-over-genre-over-tag isn't the right read here — a REVIEW should name every constraint
 * the rule opinions about, not just the most specific one (contrast the taste inspector's own
 * single-label `PredicateSummary`, SPEC F86.6). */
export function describeTastePredicate(predicate: PersonaCardReviewTastePredicate): string {
  const parts: string[] = [];
  if (predicate.artist !== null) parts.push(`artist: ${predicate.artist}`);
  if (predicate.genre !== null) parts.push(`genre: ${predicate.genre}`);
  if (predicate.tag !== null) parts.push(`tag: ${predicate.tag}`);
  return parts.length > 0 ? parts.join(", ") : "any track";
}

export function describeTasteContext(context: PersonaCardReviewTasteContext): string {
  const parts: string[] = [];
  if (context.daysOfWeek.length > 0) {
    parts.push(context.daysOfWeek.map((day) => DAY_LABELS[day] ?? "?").join(", "));
  }
  if (context.startHour !== null && context.endHour !== null) {
    parts.push(
      `${String(context.startHour).padStart(2, "0")}:00–${String(context.endHour).padStart(2, "0")}:00`
    );
  }
  return parts.length > 0 ? parts.join(" · ") : "any time";
}

/**
 * A hostile/malformed card could carry a weight wildly outside SPEC F82.1's `[-1, 1]` contract —
 * the server (`TasteRule`'s own constructor guard) is the only place that actually ENFORCES that
 * range. This gate's job is fidelity, not enforcement (review follow-up #2): it renders the TRUE
 * value the card carries, with an honest "(out of range)" flag when it falls outside `[-1, 1]`,
 * rather than quietly reformatting it into a number the card never said. The only thing capped is
 * READABILITY — a magnitude too large to read as ordinary decimal digits (an absurd/hostile card,
 * e.g. `1e21`) switches to exponential notation instead of printing a 22-digit string; the number
 * itself is never altered, only how many digits represent it on screen.
 */
const WEIGHT_RANGE_MIN = -1;
const WEIGHT_RANGE_MAX = 1;
const WEIGHT_READABLE_MAGNITUDE = 1_000_000;

export function formatWeight(weight: number): string {
  const magnitude = Math.abs(weight);
  const sign = weight < 0 ? "-" : "+";
  const digits =
    magnitude >= WEIGHT_READABLE_MAGNITUDE ? magnitude.toExponential(2) : magnitude.toFixed(2);
  const outOfRange = weight < WEIGHT_RANGE_MIN || weight > WEIGHT_RANGE_MAX;
  const value = `${sign}${digits}`;
  return outOfRange ? `${value} (out of range)` : value;
}
