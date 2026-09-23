// Server-side wire shapes + narrowing for GET /api/about (SPEC F207.1, F207.2; STORY-474; PLAN
// T561) — the About page's own facts in one round-trip: the build-stamped version, station name +
// tagline, library count, process uptime, and the F169 attribution list. `AboutController` and
// `AttributionsController` share one `AttributionProjector` server-side (PLAN T561's own
// extraction), so this module's `Attribution*Dto` shapes are the SAME shape `GET /api/attributions`
// serves — a future caller of that endpoint directly should import these rather than redeclaring
// them a second time.
//
// Fetched via `lib/api.ts`'s `apiGet` from `app/(authed)/about/page.tsx` (a Server Component) —
// this module owns only the wire shape + narrowing, never the fetch itself, mirroring
// `gardener-api.ts`'s own split between wire types and the page's own `apiGet` call.

/** One credit line (SPEC F169.2) — `creator`/`sourceUrl` are absent for some pack kinds (a font
 * pack's own single aggregate line, a voice pack's synthetic-blend line). */
export interface AttributionLineDto {
  title: string;
  creator: string | null;
  sourceUrl: string | null;
  license: string;
}

/** One installed pack's own credits (SPEC F169.2). */
export interface AttributionPackDto {
  slug: string;
  name: string;
  attributions: AttributionLineDto[];
}

/** One kind's packs (SPEC F169.2) — `kind` is one of `"jingle-pack"`, `"font-pack"`,
 * `"voice-pack"` on the wire (`AttributionsController`'s own fixed group order); kept as a bare
 * `string` here rather than a literal union so an unrecognized future kind still renders (under
 * its own raw label) instead of failing this module's narrowing outright. */
export interface AttributionGroupDto {
  kind: string;
  packs: AttributionPackDto[];
}

/** `GET /api/attributions`'s own response shape (SPEC F169.2) — empty groups are never sent
 * (`AttributionsController`'s own remarks), so `groups` may legitimately be `[]` when no pack of
 * any kind is installed. */
export interface AttributionsResponseDto {
  groups: AttributionGroupDto[];
}

/** `GET /api/about`'s own response shape (SPEC F207.1, F207.2). `tagline` is `""` when
 * `Station:Tagline` is unset — the page renders it only when non-blank. */
export interface AboutResponseDto {
  version: string;
  stationName: string;
  tagline: string;
  libraryCount: number;
  uptimeSeconds: number;
  attributions: AttributionsResponseDto;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isAttributionLineDto(raw: unknown): raw is AttributionLineDto {
  if (!isRecord(raw)) return false;
  return (
    typeof raw["title"] === "string" &&
    (raw["creator"] === null || typeof raw["creator"] === "string") &&
    (raw["sourceUrl"] === null || typeof raw["sourceUrl"] === "string") &&
    typeof raw["license"] === "string"
  );
}

function isAttributionPackDto(raw: unknown): raw is AttributionPackDto {
  if (!isRecord(raw)) return false;
  return (
    typeof raw["slug"] === "string" &&
    typeof raw["name"] === "string" &&
    Array.isArray(raw["attributions"]) &&
    raw["attributions"].every(isAttributionLineDto)
  );
}

function isAttributionGroupDto(raw: unknown): raw is AttributionGroupDto {
  if (!isRecord(raw)) return false;
  return (
    typeof raw["kind"] === "string" &&
    Array.isArray(raw["packs"]) &&
    raw["packs"].every(isAttributionPackDto)
  );
}

function isAttributionsResponseDto(raw: unknown): raw is AttributionsResponseDto {
  if (!isRecord(raw)) return false;
  return Array.isArray(raw["groups"]) && raw["groups"].every(isAttributionGroupDto);
}

/** Narrows an unknown 2xx body to {@link AboutResponseDto} (repo's "no casts of unknown JSON"
 * boundary rule — mirrors `gardener-api.ts`'s `isFileActionPlanDto`) — walks the full nested
 * attribution shape too, so a malformed or future-shaped body degrades to the page's own "unable
 * to load" state rather than an `undefined`-riddled render. */
export function isAboutResponseDto(raw: unknown): raw is AboutResponseDto {
  if (!isRecord(raw)) return false;
  return (
    typeof raw["version"] === "string" &&
    typeof raw["stationName"] === "string" &&
    typeof raw["tagline"] === "string" &&
    typeof raw["libraryCount"] === "number" &&
    typeof raw["uptimeSeconds"] === "number" &&
    isAttributionsResponseDto(raw["attributions"])
  );
}
