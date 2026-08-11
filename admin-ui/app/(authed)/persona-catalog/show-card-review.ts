/**
 * Client-side, NON-authoritative read of a show manifest's FULL text (SPEC F118.2; ARCHITECTURE.md
 * "Trust ruling") — feeds `ShowCardReviewModal`, the show kind's own required review-before-import
 * stop. Mirrors `persona-card-review.ts`'s own `parsePersonaCardReview` shape and tolerance rules,
 * narrowed to `ShowManifest`'s own much smaller `{name, tagline, flavor}` schema (Host's
 * `GenWave.Host.Shows.ShowManifest`) — there is no persona-card-sized "advanced fields" surface
 * here to collect into an `otherFields` bag: a show manifest's whole authored content IS these
 * three fields.
 *
 * The server (`ShowManifestParser.Parse` + `ShowsController.Import`) remains the only validator —
 * a manifest that fails to parse here still gets a real import attempt against the raw bytes this
 * module never touches, and `ShowCardReviewModal` never sends what it parses, only the original
 * card text byte-for-byte. This only ever degrades to `null` (an unparsable manifest, or one
 * missing a usable `name`), never throws — `tagline`/`flavor` tolerate a missing/wrong-typed value
 * by falling back to `""` (SPEC F79.2's forward-compat posture, mirrored).
 */

export interface ShowCardReview {
  name: string;
  tagline: string;
  flavor: string;
}

interface RawShowCard {
  name?: unknown;
  tagline?: unknown;
  flavor?: unknown;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function asString(value: unknown, fallback = ""): string {
  return typeof value === "string" ? value : fallback;
}

/**
 * Parses `json` into the full review projection. Returns `null` for anything that isn't valid
 * JSON, isn't an object, or is missing a usable `name` — the one field load-bearing enough to fail
 * the whole review on (nothing to show the operator without it). `tagline`/`flavor` degrade to `""`
 * rather than failing the parse.
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
  };
}
