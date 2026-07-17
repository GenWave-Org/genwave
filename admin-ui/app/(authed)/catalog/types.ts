/** Shape of a GET /api/media row rendered in the catalog table. */
export interface AdminMediaDto {
  mediaId: string;
  locator: string;
  format: string;
  state: string;
  durationMs: number | null;
  title: string | null;
  artist: string | null;
  album: string | null;
  genre: string | null;
  year: number | null;
  /** Tempo estimate in BPM, one decimal (SPEC F46.1, F49.2) — null until analyzed. */
  bpm: number | null;
  /** Whole-track perceptual energy, 0–1 (SPEC F47.1, F49.2) — null until measured. */
  trackEnergy: number | null;
  integratedLufs: number | null;
  truePeakDbtp: number | null;
  measurable: boolean | null;
  cueInSec: number | null;
  cueOutSec: number | null;
  eligible: boolean;
  /** Postgres xmin, serialized as a string — forwarded as `If-Match: W/"<version>"` on the
   * shipped single-row PATCH when the catalog toolbar's selection mode targets this row (SPEC
   * F28.11, Q7 review Finding 1). */
  version: string;
  /** Rating state (SPEC F33.10) — resolved server-side via LEFT JOIN + COALESCE against
   * `library.media_rating`; an unrated row reads the F33.2 ledger default (score 50, not
   * flagged). Never influences `version`/the ETag (F33.1) — a vote or never-play toggle doesn't
   * invalidate an open edit form. */
  score: number;
  neverPlay: boolean;
}

/** Parsed `X-Pagination: total=…,pages=…,page=…,limit=…` header. */
export interface Pagination {
  total: number;
  pages: number;
  page: number;
  limit: number;
}

/**
 * Mirror of the backend bulk filter shapes (`BulkEligibilityFilter` /
 * `BulkReassignFilter` / `BulkReenrichFilter`) — all fields optional/null.
 * This is the byte-compatibility oracle inherited from the retired
 * Bulk*Control components: CatalogToolbar sends this exact shape in
 * by-filter mode (empty selection, active filter). In selection mode it is
 * unused — each selected row is written individually via the shipped
 * single-row endpoints (SPEC F28.11, Q7 review Finding 1).
 */
export interface BulkFilter {
  state: string | null;
  artist: string | null;
  genre: string | null;
  libraryId: number | null;
  q: string | null;
  /** null = no eligible predicate in the filter (matches all rows regardless of eligibility). */
  eligible: boolean | null;
  /**
   * Case-insensitive exact-match filters fed by the Catalog facet pickers (SPEC F52.3–F52.5).
   * Optional (not just nullable) so every pre-Y3 `BulkFilter` object literal across the test
   * suite keeps compiling unchanged — `undefined`/`null`/`[]` all mean "no filter" and the
   * backend ignores them identically either way (`MediaRepository.BuildAdminWhere`). page.tsx,
   * the one production producer, always sets all three explicitly.
   */
  artistExact?: string | null;
  /** No album substring filter exists (`q` already searches album, F52.3) — this is album's only mode. */
  albumExact?: string | null;
  /** OR-matched across values (SPEC F52.3); empty/omitted applies no filter. */
  genresExact?: string[];
}

/** One row from `GET /api/media/facets?field=artist|album|genre` (SPEC F52.1) — camelCase,
 * case-insensitively grouped by the backend; `count` is the group's row total under effective
 * scope (F52.2). */
export interface FacetOption {
  value: string;
  count: number;
}
