import type { ReactNode } from "react";
import { cookies } from "next/headers";
import Link from "next/link";
import { apiGet } from "@/lib/api";
import type { LibraryDto } from "@/lib/library";
import { CatalogTabs, type CatalogTab } from "./CatalogTabs";
import { CatalogTable } from "./CatalogTable";
import { LibrariesTab } from "./LibrariesTab";
import { PurgeUnavailableAction } from "./PurgeUnavailableAction";
import { YearFilterControl } from "./YearFilterControl";
import { FacetFilterControl } from "./FacetFilterControl";
import { MoodFilterControl } from "./MoodFilterControl";
import { Pager } from "@/components/ui/pager";
import { Tooltip } from "@/components/ui/tooltip";
import type { AdminMediaDto, BulkFilter, Pagination } from "./types";

// Catalog data changes via the bulk toolbar and library CRUD — always render fresh.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

interface MediaSearchParams {
  tab?: string;
  page?: string;
  q?: string;
  state?: string;
  artist?: string;
  genre?: string;
  "library-id"?: string;
  eligible?: string;
  "never-play"?: string;
  decade?: string;
  "year-missing"?: string;
  /** SPEC F52.3 exact-match filters, fed by the facet pickers (F52.5). `genre-exact` is
   * repeatable — Next.js hands back an array once more than one instance is on the URL, a bare
   * string otherwise; {@link genreExactValues} normalizes both shapes. */
  "artist-exact"?: string;
  "album-exact"?: string;
  "genre-exact"?: string | string[];
  /** SPEC F86.8 mood-exact filter, fed by the mood picker (fixed `MoodVocabulary`, no facet
   * fetch). Repeatable like `genre-exact`; {@link moodExactValues} normalizes both shapes. */
  "mood-exact"?: string | string[];
  /** gh-#113 — "true" reveals unavailable rows the default view hides (mapped straight onto the
   * API's `include-unavailable` param). Any other value (or absent) keeps the default view. */
  "include-unavailable"?: string;
}

interface CatalogPageProps {
  searchParams: Promise<MediaSearchParams>;
}

/**
 * True when the browse response carries `X-Out-Of-Scope: true` — set by the API only for
 * a named-library browse (`?library-id=`) whose library sits outside
 * `Station:Scope:LibraryIds` (SPEC F23.2). The rows still render; this only drives the
 * "out of rotation" badge so the operator knows why they don't hear these tracks.
 */
function readOutOfScopeHeader(resp: Response): boolean {
  return resp.headers.get("X-Out-Of-Scope") === "true";
}

/**
 * The `X-Unavailable-Hidden` count (gh-#113) — set by the API only when the default view hid
 * unavailable rows: how many the browse's other filters matched but the page excluded. Absent
 * (or malformed) reads as 0, which also covers the revealed view and explicit state filters.
 */
function readUnavailableHiddenHeader(resp: Response): number {
  const raw = resp.headers.get("X-Unavailable-Hidden");
  if (raw === null) return 0;
  const parsed = parseInt(raw, 10);
  return isNaN(parsed) || parsed < 0 ? 0 : parsed;
}

function parsePaginationHeader(header: string): Pagination {
  const parts = header.split(",");
  const map: Record<string, number> = {};
  for (const part of parts) {
    const [key, val] = part.trim().split("=");
    if (key !== undefined && val !== undefined) {
      const parsed = parseInt(val, 10);
      if (!isNaN(parsed)) {
        map[key] = parsed;
      }
    }
  }
  return {
    total: map["total"] ?? 0,
    pages: map["pages"] ?? 1,
    page: map["page"] ?? 1,
    limit: map["limit"] ?? 50,
  };
}

/** Normalizes `sp["genre-exact"]` (bare string once, string[] once repeated, undefined when
 * absent — Next.js's own shape for a repeatable query param) into a clean, blank-filtered list.
 * Filters whitespace-only entries too, not just exact `""` — a stray empty sibling on the native
 * GET form (SPEC F52.4 defect fix) must drop out the same way an outright-absent one does. */
function genreExactValues(sp: MediaSearchParams): string[] {
  const raw = sp["genre-exact"];
  if (raw === undefined) return [];
  const list = Array.isArray(raw) ? raw : [raw];
  return list.filter((v) => v.trim() !== "");
}

/** Same normalization as {@link genreExactValues}, for `mood-exact` (SPEC F86.8). */
function moodExactValues(sp: MediaSearchParams): string[] {
  const raw = sp["mood-exact"];
  if (raw === undefined) return [];
  const list = Array.isArray(raw) ? raw : [raw];
  return list.filter((v) => v.trim() !== "");
}

/**
 * Normalizes one URL-parsed string filter value for the bulk-write body (SPEC F52.4 defect fix —
 * reproduced live: `?artist-exact=Queen` sent a bulk body with `"albumExact":""`, which
 * `BuildAdminWhere` read as a real-but-unmatchable exact filter and zeroed out the sweep). A
 * native GET form serializes every sibling input, so an unfilled field arrives as `""`, not
 * `undefined` — this collapses blank/whitespace-only back to `null` so the bulk body agrees with
 * what the browse path already returns (`appendFilterParams` strips these the same way).
 */
function normalizeFilterValue(value: string | undefined): string | null {
  if (value === undefined || value.trim() === "") return null;
  return value;
}

type FilterField =
  | "q"
  | "state"
  | "artist"
  | "genre"
  | "library-id"
  | "eligible"
  | "never-play"
  | "decade"
  | "year-missing"
  | "artist-exact"
  | "album-exact"
  | "genre-exact"
  | "mood-exact"
  | "include-unavailable";

interface AppendFilterParamsOptions {
  /** Filter fields to leave out entirely — the active-filter chips' "clear this one" links. */
  omit?: Set<FilterField>;
  /** Drops just this one value out of the (possibly multi-valued) genre-exact set, keeping the
   * rest — clearing a single genre chip must not also drop every other picked genre. */
  omitGenreExactValue?: string;
  /** Same as {@link omitGenreExactValue}, for mood-exact (SPEC F86.8). */
  omitMoodExactValue?: string;
}

/**
 * Appends every active filter field (not pagination) onto `query` — the one place all four
 * catalog URL builders below assemble the shared filter param set, so adding SPEC F52.3's three
 * exact-match fields (or any future filter) only has to happen once.
 */
function appendFilterParams(query: URLSearchParams, sp: MediaSearchParams, options: AppendFilterParamsOptions = {}): void {
  const omit = options.omit ?? new Set<FilterField>();
  if (!omit.has("q") && sp.q !== undefined) query.set("q", sp.q);
  if (!omit.has("state") && sp.state !== undefined) query.set("state", sp.state);
  if (!omit.has("artist") && sp.artist !== undefined) query.set("artist", sp.artist);
  if (!omit.has("genre") && sp.genre !== undefined) query.set("genre", sp.genre);
  if (!omit.has("library-id") && sp["library-id"] !== undefined) query.set("library-id", sp["library-id"]);
  if (!omit.has("eligible") && sp.eligible !== undefined) query.set("eligible", sp.eligible);
  if (!omit.has("never-play") && sp["never-play"] !== undefined) query.set("never-play", sp["never-play"]);
  if (!omit.has("decade") && sp.decade !== undefined && sp.decade !== "") query.set("decade", sp.decade);
  if (!omit.has("year-missing") && sp["year-missing"] !== undefined) query.set("year-missing", sp["year-missing"]);
  if (!omit.has("artist-exact") && sp["artist-exact"] !== undefined && sp["artist-exact"] !== "") {
    query.set("artist-exact", sp["artist-exact"]);
  }
  if (!omit.has("album-exact") && sp["album-exact"] !== undefined && sp["album-exact"] !== "") {
    query.set("album-exact", sp["album-exact"]);
  }
  if (!omit.has("genre-exact")) {
    for (const value of genreExactValues(sp)) {
      if (value !== options.omitGenreExactValue) query.append("genre-exact", value);
    }
  }
  if (!omit.has("mood-exact")) {
    for (const value of moodExactValues(sp)) {
      if (value !== options.omitMoodExactValue) query.append("mood-exact", value);
    }
  }
  // gh-#113 — only the literal "true" is meaningful (absent/false both mean the default view),
  // so nothing else is ever propagated onto pagination or chip-clear URLs.
  if (!omit.has("include-unavailable") && sp["include-unavailable"] === "true") {
    query.set("include-unavailable", "true");
  }
}

/** The catalog URL with unavailable rows revealed (gh-#113), every active filter kept and the
 * page reset to 1 — revealing rows changes the row set, so a deep page number is meaningless. */
function buildShowUnavailableUrl(sp: MediaSearchParams): string {
  const query = new URLSearchParams();
  appendFilterParams(query, sp);
  query.set("include-unavailable", "true");
  return `/catalog?${query.toString()}`;
}

/** The catalog URL back to the default (hidden) view (gh-#113) — {@link buildShowUnavailableUrl}'s
 * inverse, with the same page-1 reset. */
function buildHideUnavailableUrl(sp: MediaSearchParams): string {
  const query = new URLSearchParams();
  appendFilterParams(query, sp, { omit: new Set<FilterField>(["include-unavailable"]) });
  const qs = query.toString();
  return `/catalog${qs ? `?${qs}` : ""}`;
}

function buildMediaUrl(sp: MediaSearchParams): string {
  const query = new URLSearchParams();
  if (sp.page !== undefined) query.set("page", sp.page);
  appendFilterParams(query, sp);
  const qs = query.toString();
  return `/api/media${qs ? `?${qs}` : ""}`;
}

function buildPageUrl(sp: MediaSearchParams, page: number): string {
  const query = new URLSearchParams();
  query.set("page", String(page));
  appendFilterParams(query, sp);
  return `/catalog?${query.toString()}`;
}

/**
 * The catalog URL with the decade/year-missing filter removed, every other active filter kept
 * (SPEC F49.4) — the active-filter chip's clear link. Navigating here drops the chip and the
 * query param together in one round trip: no client JS needed since the chip is a plain anchor.
 */
function buildClearYearFilterUrl(sp: MediaSearchParams): string {
  const query = new URLSearchParams();
  appendFilterParams(query, sp, { omit: new Set<FilterField>(["decade", "year-missing"]) });
  const qs = query.toString();
  return `/catalog${qs ? `?${qs}` : ""}`;
}

/** The catalog URL with the artist-exact filter removed, every other active filter kept (SPEC
 * F52.5) — the artist facet chip's clear link. */
function buildClearArtistExactUrl(sp: MediaSearchParams): string {
  const query = new URLSearchParams();
  appendFilterParams(query, sp, { omit: new Set<FilterField>(["artist-exact"]) });
  const qs = query.toString();
  return `/catalog${qs ? `?${qs}` : ""}`;
}

/** The catalog URL with the album-exact filter removed, every other active filter kept (SPEC
 * F52.5) — the album facet chip's clear link. */
function buildClearAlbumExactUrl(sp: MediaSearchParams): string {
  const query = new URLSearchParams();
  appendFilterParams(query, sp, { omit: new Set<FilterField>(["album-exact"]) });
  const qs = query.toString();
  return `/catalog${qs ? `?${qs}` : ""}`;
}

/** The catalog URL with just one picked genre removed, the rest of a multi-genre selection (and
 * every other active filter) kept (SPEC F52.5) — one genre chip's clear link. */
function buildClearGenreExactValueUrl(sp: MediaSearchParams, value: string): string {
  const query = new URLSearchParams();
  appendFilterParams(query, sp, { omitGenreExactValue: value });
  const qs = query.toString();
  return `/catalog${qs ? `?${qs}` : ""}`;
}

/** The catalog URL with just one picked mood removed, the rest of a multi-mood selection (and
 * every other active filter) kept (SPEC F86.8) — one mood chip's clear link, mirroring
 * {@link buildClearGenreExactValueUrl}. */
function buildClearMoodExactValueUrl(sp: MediaSearchParams, value: string): string {
  const query = new URLSearchParams();
  appendFilterParams(query, sp, { omitMoodExactValue: value });
  const qs = query.toString();
  return `/catalog${qs ? `?${qs}` : ""}`;
}

interface YearFilterChip {
  label: string;
}

/** The active decade/year-missing filter, if any, for the F49.4 filter chip — null when neither
 * is set (the API's own mutual-exclusivity rule means at most one of these is ever active). */
function resolveYearFilterChip(sp: MediaSearchParams): YearFilterChip | null {
  if (sp.decade !== undefined && sp.decade !== "") return { label: `${sp.decade}s` };
  if (sp["year-missing"] === "true") return { label: "Missing year" };
  return null;
}

/**
 * True when a search/filter field is actively narrowing results — distinguishes an empty catalog
 * from an empty *filtered* result (drives the CatalogTable EmptyState choice). Includes the
 * never-play filter (SPEC F33.10) for that purpose, but NOT for by-filter bulk-toolbar
 * activation: CatalogTable derives that separately from `bulkFilter`'s own fields
 * (`hasBulkFilter`), since the bulk write endpoints take no `neverPlay` filter at all (F33.7) —
 * an active never-play-only browse must never enable a by-filter bulk action.
 */
function isFilterActive(sp: MediaSearchParams): boolean {
  return (
    (sp.q !== undefined && sp.q !== "") ||
    (sp.state !== undefined && sp.state !== "") ||
    (sp.artist !== undefined && sp.artist !== "") ||
    (sp.genre !== undefined && sp.genre !== "") ||
    sp["library-id"] !== undefined ||
    (sp.eligible !== undefined && sp.eligible !== "") ||
    sp["never-play"] === "true" ||
    (sp.decade !== undefined && sp.decade !== "") ||
    sp["year-missing"] === "true" ||
    (sp["artist-exact"] !== undefined && sp["artist-exact"] !== "") ||
    (sp["album-exact"] !== undefined && sp["album-exact"] !== "") ||
    genreExactValues(sp).length > 0 ||
    moodExactValues(sp).length > 0
  );
}

interface FilterChip {
  key: string;
  label: string;
  clearHref: string;
}

/**
 * Every active filter chip (SPEC F49.4, widened by F52.5 to the three facet-picker filters) — the
 * year/missing-year chip plus one chip per exact-match value, each with its own scoped clear link
 * (clearing one genre out of a multi-genre pick keeps the rest, SPEC F52.5).
 */
function resolveFilterChips(sp: MediaSearchParams): FilterChip[] {
  const chips: FilterChip[] = [];

  const yearChip = resolveYearFilterChip(sp);
  if (yearChip !== null) {
    chips.push({ key: "year", label: yearChip.label, clearHref: buildClearYearFilterUrl(sp) });
  }
  if (sp["artist-exact"] !== undefined && sp["artist-exact"] !== "") {
    chips.push({
      key: "artist-exact",
      label: `Artist: ${sp["artist-exact"]}`,
      clearHref: buildClearArtistExactUrl(sp),
    });
  }
  if (sp["album-exact"] !== undefined && sp["album-exact"] !== "") {
    chips.push({
      key: "album-exact",
      label: `Album: ${sp["album-exact"]}`,
      clearHref: buildClearAlbumExactUrl(sp),
    });
  }
  for (const value of genreExactValues(sp)) {
    chips.push({
      key: `genre-exact:${value}`,
      label: `Genre: ${value}`,
      clearHref: buildClearGenreExactValueUrl(sp, value),
    });
  }
  for (const value of moodExactValues(sp)) {
    chips.push({
      key: `mood-exact:${value}`,
      label: `Mood: ${value}`,
      clearHref: buildClearMoodExactValueUrl(sp, value),
    });
  }

  return chips;
}

function resolveTab(sp: MediaSearchParams): CatalogTab {
  return sp.tab === "libraries" ? "libraries" : "tracks";
}

const PAGE_TITLE = (
  <h1 className="font-display text-[1.35rem] font-semibold text-ink">Catalog</h1>
);

export default async function CatalogPage({ searchParams }: CatalogPageProps): Promise<ReactNode> {
  const sp = await searchParams;
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();
  const activeTab = resolveTab(sp);

  const librariesResp = await apiGet("/api/libraries", { cookies: cookieHeader });
  // Libraries are best-effort on the Tracks tab (reassign hides itself when none available);
  // the Libraries tab itself surfaces its own load failure below.
  const libraries: LibraryDto[] = librariesResp.ok ? ((await librariesResp.json()) as LibraryDto[]) : [];

  if (activeTab === "libraries") {
    return (
      <main>
        {PAGE_TITLE}
        <div className="mt-4">
          <CatalogTabs activeTab={activeTab} />
        </div>
        <div className="mt-6">
          {librariesResp.ok ? (
            <LibrariesTab initialLibraries={libraries} />
          ) : (
            <p className="text-[0.85rem] text-danger">Unable to load libraries.</p>
          )}
        </div>
      </main>
    );
  }

  const url = buildMediaUrl(sp);
  const resp = await apiGet(url, { cookies: cookieHeader });

  if (!resp.ok) {
    return (
      <main>
        {PAGE_TITLE}
        <div className="mt-4">
          <CatalogTabs activeTab={activeTab} />
        </div>
        <p className="mt-6 text-[0.85rem] text-danger">Unable to load media.</p>
      </main>
    );
  }

  const media = (await resp.json()) as AdminMediaDto[];
  const paginationRaw = resp.headers.get("X-Pagination") ?? "";
  const pagination = parsePaginationHeader(paginationRaw);
  const currentPage = pagination.page;
  const outOfScope = readOutOfScopeHeader(resp);
  const filterActive = isFilterActive(sp);
  const filterChips = resolveFilterChips(sp);
  // gh-#113 — the two halves of the hidden-unavailable line: how many rows the default view hid
  // (API-counted, 0 whenever hiding was off), and whether this browse has them revealed.
  const unavailableHidden = readUnavailableHiddenHeader(resp);
  const showingUnavailable = sp["include-unavailable"] === "true";

  const bulkFilter: BulkFilter = {
    // Every string field is run through normalizeFilterValue — the catalog filter form submits
    // every field on a bare GET, so an unfilled sibling arrives as `""`, not `undefined` (SPEC
    // F52.4 defect fix; see normalizeFilterValue's own doc for the reproduced 0-affected repro).
    state: normalizeFilterValue(sp.state),
    artist: normalizeFilterValue(sp.artist),
    genre: normalizeFilterValue(sp.genre),
    libraryId: sp["library-id"] !== undefined ? parseInt(sp["library-id"], 10) : null,
    q: normalizeFilterValue(sp.q),
    eligible: sp.eligible === "true" ? true : sp.eligible === "false" ? false : null,
    artistExact: normalizeFilterValue(sp["artist-exact"]),
    albumExact: normalizeFilterValue(sp["album-exact"]),
    genresExact: genreExactValues(sp),
    // No moodsExact here, deliberately: the bulk write endpoints (BulkReassign/Eligibility/
    // Reenrich/Rating) never grew a mood-exact filter field alongside T79's browse-only one — a
    // mood-only browse must not light up by-filter bulk mode over a set the bulk endpoints can't
    // actually narrow by, mirroring the never-play precedent (F33.7, hasBulkFilter's own doc).
  };

  return (
    <main>
      {PAGE_TITLE}

      <div className="mt-4">
        <CatalogTabs activeTab={activeTab} />
      </div>

      <form method="get" className="mt-4 flex flex-wrap items-end gap-3">
        <div className="flex flex-col gap-1.5">
          <label htmlFor="q" className="text-[0.82rem] font-semibold text-mute">
            Search
          </label>
          <input
            id="q"
            name="q"
            type="search"
            defaultValue={sp.q ?? ""}
            className="h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink"
          />
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="state" className="text-[0.82rem] font-semibold text-mute">
            State
          </label>
          <select
            id="state"
            name="state"
            defaultValue={sp.state ?? ""}
            className="h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink"
          >
            <option value="">All</option>
            <option value="ready">Ready</option>
            <option value="pending">Pending</option>
            <option value="error">Error</option>
          </select>
        </div>

        <FacetFilterControl
          field="artist"
          label="Artist"
          exactParamName="artist-exact"
          substringParamName="artist"
          multiple={false}
          initialSubstringValue={sp.artist}
          initialExactValues={sp["artist-exact"] !== undefined && sp["artist-exact"] !== "" ? [sp["artist-exact"]] : []}
          libraryId={sp["library-id"]}
        />

        <FacetFilterControl
          field="album"
          label="Album"
          exactParamName="album-exact"
          multiple={false}
          initialExactValues={sp["album-exact"] !== undefined && sp["album-exact"] !== "" ? [sp["album-exact"]] : []}
          libraryId={sp["library-id"]}
        />

        <FacetFilterControl
          field="genre"
          label="Genre"
          exactParamName="genre-exact"
          substringParamName="genre"
          multiple={true}
          initialSubstringValue={sp.genre}
          initialExactValues={genreExactValues(sp)}
          libraryId={sp["library-id"]}
        />

        <MoodFilterControl initialValues={moodExactValues(sp)} />

        <div className="flex flex-col gap-1.5">
          <label htmlFor="eligible" className="text-[0.82rem] font-semibold text-mute">
            Eligibility
          </label>
          <select
            id="eligible"
            name="eligible"
            defaultValue={sp.eligible ?? ""}
            className="h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink"
          >
            <option value="">All</option>
            <option value="true">Eligible</option>
            <option value="false">Ineligible</option>
          </select>
        </div>

        <div className="flex flex-col gap-1.5">
          <span className="text-[0.82rem] font-semibold text-mute">Rating</span>
          {/* Unchecked checkboxes are omitted from a GET form submission by the browser itself —
              that IS the F33.10 "absent/off = no param" contract, no extra JS required. */}
          <label
            htmlFor="never-play"
            className="flex h-9 min-h-10 items-center gap-1.5 text-[0.85rem] text-ink"
          >
            <input
              id="never-play"
              name="never-play"
              type="checkbox"
              value="true"
              defaultChecked={sp["never-play"] === "true"}
            />
            Never-play only
          </label>
        </div>

        <YearFilterControl initialDecade={sp.decade} initialYearMissing={sp["year-missing"]} />

        {sp["library-id"] !== undefined && (
          <input type="hidden" name="library-id" value={sp["library-id"]} />
        )}

        <button
          type="submit"
          className="h-9 rounded-[6px] bg-accent px-4 text-[0.85rem] font-semibold text-accent-ink hover:bg-accent/90"
        >
          Filter
        </button>
      </form>

      {filterChips.length > 0 && (
        <div className="mt-3 flex flex-wrap gap-2">
          {filterChips.map((chip) => (
            <span
              key={chip.key}
              className="inline-flex items-center gap-1.5 rounded-[3px] border border-accent-2 px-1.5 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-accent-2"
            >
              {chip.label}
              <Tooltip label={`Clear ${chip.label} filter`}>
                <Link href={chip.clearHref} aria-label={`Clear ${chip.label} filter`} className="hover:text-ink">
                  ×
                </Link>
              </Tooltip>
            </span>
          ))}
        </div>
      )}

      {outOfScope && (
        <p role="status" className="mt-3 text-[0.82rem] text-mute">
          <span aria-label="out of rotation" className="font-semibold text-accent-2">
            Out of rotation
          </span>{" "}
          — library {sp["library-id"]} is not in the station&apos;s rotation scope; these rows are
          parked, not currently playing.
        </p>
      )}

      <p className="mt-3 text-[0.82rem] text-mute">
        {pagination.total} track{pagination.total === 1 ? "" : "s"} found
        {pagination.pages > 1 ? ` — page ${currentPage} of ${pagination.pages}` : ""}
      </p>

      {/* gh-#113 — unavailable rows are hidden by default; name the count and offer the reveal.
          Plain anchors, no client JS — the toggle is just this page with include-unavailable=true.
          The revealed view (and an explicit state=unavailable browse) also carries the explicit
          operator purge, the only destructive path for these rows. */}
      {showingUnavailable || sp.state === "unavailable" ? (
        <div className="mt-2 flex flex-wrap items-center gap-3">
          {showingUnavailable && (
            <p className="text-[0.82rem] text-mute">
              Showing unavailable tracks.{" "}
              <Link href={buildHideUnavailableUrl(sp)} className="text-accent hover:underline">
                Hide unavailable
              </Link>
            </p>
          )}
          <PurgeUnavailableAction />
        </div>
      ) : unavailableHidden > 0 ? (
        <p className="mt-1 text-[0.82rem] text-mute">
          {unavailableHidden} unavailable track{unavailableHidden === 1 ? "" : "s"} hidden.{" "}
          <Link href={buildShowUnavailableUrl(sp)} className="text-accent hover:underline">
            Show unavailable
          </Link>
        </p>
      ) : null}

      <CatalogTable
        media={media}
        pagination={pagination}
        libraries={libraries}
        bulkFilter={bulkFilter}
        filterActive={filterActive}
        clearFiltersHref="/catalog"
      />

      <Pager page={currentPage} pages={pagination.pages} hrefFor={(page) => buildPageUrl(sp, page)} />
    </main>
  );
}
