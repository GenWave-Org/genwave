/**
 * Client-side, NON-authoritative read of a show manifest's FULL text (SPEC F118.2, F152.6;
 * ARCHITECTURE.md "Trust ruling") — feeds `ShowCardReviewModal`, the show kind's own required
 * review-before-import stop. Mirrors `persona-card-review.ts`'s own `parsePersonaCardReview` shape
 * and tolerance rules, narrowed to `ShowManifest`'s own small `{name, tagline, flavor, envelope}`
 * schema (Host's `GenWave.Host.Shows.ShowManifest`) — there is no persona-card-sized "advanced
 * fields" surface here to collect into an `otherFields` bag: a show manifest's whole authored
 * content is these three fields, plus the schema 1.1 `envelope.rotation` addition (PLAN T363).
 *
 * The server (`ShowManifestParser.Parse` + `ShowsController.Import`) remains the only validator —
 * a manifest that fails to parse here still gets a real import attempt against the raw bytes this
 * module never touches, and `ShowCardReviewModal` never sends what it parses, only the original
 * card text byte-for-byte. This only ever degrades to `null` (an unparsable manifest, or one
 * missing a usable `name`), never throws — `tagline`/`flavor` tolerate a missing/wrong-typed value
 * by falling back to `""` (SPEC F79.2's forward-compat posture, mirrored); `rotation` tolerates a
 * missing/malformed `envelope`/`envelope.rotation` by falling back to `null` — "no rule to show,"
 * never a parse failure — the server's own `ShowManifestParser.ParseEnvelope` is the ONE place a
 * malformed rotation actually refuses an import (400); this display-only read never re-derives
 * those bound checks (at least one of `maxPlays`/`notAiredWithinDays`, `maxPlays` ≥ 0,
 * `notAiredWithinDays` 1–3650).
 */

import type { RotationPredicateDto } from "@/lib/shows-rotation-api";

export interface ShowCardReview {
  name: string;
  tagline: string;
  flavor: string;
  /** The manifest's own `envelope.rotation`, if any (SPEC F152.6) — `null` for a 1.0 manifest, an
   * `envelope` with no `rotation`, or a `rotation` this client-side read cannot make sense of.
   * Purely display: `ShowCardReviewModal`'s confirm never edits or strips it — the ORIGINAL card
   * text is what gets POSTed, byte-for-byte. */
  rotation: RotationPredicateDto | null;
}

interface RawShowCard {
  name?: unknown;
  tagline?: unknown;
  flavor?: unknown;
  envelope?: unknown;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function asString(value: unknown, fallback = ""): string {
  return typeof value === "string" ? value : fallback;
}

function asOptionalNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

/** Reads `envelope.rotation` off the raw, untrusted `envelope` value — `null` for anything that
 * isn't an object, carries no `rotation` object, or whose `rotation` sets neither member to a
 * finite number (mirrors the server's own "both null means no rule" normalization, display-only —
 * see this module's own remarks for why the FULL server-side bound checks are never re-derived
 * here). */
function parseRotation(envelope: unknown): RotationPredicateDto | null {
  if (!isRecord(envelope) || !isRecord(envelope.rotation)) return null;
  const maxPlays = asOptionalNumber(envelope.rotation.maxPlays);
  const notAiredWithinDays = asOptionalNumber(envelope.rotation.notAiredWithinDays);
  return maxPlays === null && notAiredWithinDays === null ? null : { maxPlays, notAiredWithinDays };
}

/**
 * Parses `json` into the full review projection. Returns `null` for anything that isn't valid
 * JSON, isn't an object, or is missing a usable `name` — the one field load-bearing enough to fail
 * the whole review on (nothing to show the operator without it). `tagline`/`flavor`/`rotation`
 * degrade to `""`/`null` rather than failing the parse.
 */
export function parseShowCardReview(json: string): ShowCardReview | null {
  let raw: unknown;
  try {
    raw = JSON.parse(json);
  } catch {
    return null;
  }

  if (!isRecord(raw)) return null;
  const card = raw as RawShowCard;
  if (typeof card.name !== "string" || card.name.trim() === "") return null;

  return {
    name: card.name,
    tagline: asString(card.tagline),
    flavor: asString(card.flavor),
    rotation: parseRotation(card.envelope),
  };
}

/** "Plays tracks aired 0 times" / "Plays tracks aired at most 3 times" / "Plays tracks not aired in
 * the last 30 days" / both joined (SPEC F152.6, Dean's copy rule: sentences start with a capital) —
 * the full-card confirm's own rule line, beside flavor. `rotation` is assumed non-null
 * (`ShowCardReviewModal` only ever calls this inside its own `review.rotation !== null` guard).
 *
 * PLAN T363 review LOW-1: `maxPlays` is a CEILING (`MediaRepository.RotationPredicateSql`'s own
 * `coalesce(rot.play_count, 0) <= @maxPlays`), not an exact count — "aired N times" only reads true
 * for the `N === 0` case (a track that has aired 0 times has, trivially, aired AT MOST 0 times too);
 * every `maxPlays > 0` value needs "at most" or this line lies about the predicate. Pluralizes
 * "time(s)"/"day(s)" the same way `ShowRotationRuleEditor`'s own `poolLabel` pluralizes "track(s)". */
export function rotationRuleLine(rotation: RotationPredicateDto): string {
  const parts: string[] = [];
  if (rotation.maxPlays !== null) {
    parts.push(
      rotation.maxPlays === 0
        ? "aired 0 times"
        : `aired at most ${rotation.maxPlays} time${rotation.maxPlays === 1 ? "" : "s"}`
    );
  }
  if (rotation.notAiredWithinDays !== null) {
    parts.push(
      `not aired in the last ${rotation.notAiredWithinDays} day${rotation.notAiredWithinDays === 1 ? "" : "s"}`
    );
  }
  return `Plays tracks ${parts.join(" and ")}`;
}
