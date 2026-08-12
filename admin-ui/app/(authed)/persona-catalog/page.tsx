import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { PersonaCatalogClient } from "./PersonaCatalogClient";
import { PersonaCatalogTabs, resolveCatalogKind } from "./PersonaCatalogTabs";
import type { CatalogIndexResponseDto, ThemeCatalogProvenanceDto } from "./types";

// The underlying Community:CatalogIndexUrl setting is live-editable from Settings (SPEC F90.1),
// and the shelf itself can flip disabled<->enabled or gain/lose entries between visits — always
// re-render from the server, mirroring personas/page.tsx and safe-content/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

/**
 * Generic "GET a listing, project each admitted row's slug" fetcher (PLAN T255 review finding F3):
 * `fetchInstalledFontSlugs`/`fetchImportedShowSlugs`/`fetchHiredPersonaSlugs` below were byte-
 * identical modulo their URL and row shape before this extraction — this is the one shared
 * implementation. `predicate` (default: every row counts) is the one place a caller narrows WHICH
 * rows count — `fetchImportedShowSlugs` uses it to admit only genuinely-imported rows (review
 * finding F2), never an authored show that merely collides on the same slug. Any failure (network
 * error, non-200, or an unexpected non-array body) degrades to `[]` — fail closed: no live signal
 * ever means a slug gets FALSELY claimed.
 */
async function fetchSlugs<T extends { slug: string }>(
  path: string,
  cookieHeader: string,
  predicate: (row: T) => boolean = () => true
): Promise<string[]> {
  try {
    const response = await apiGet(path, { cookies: cookieHeader });
    if (!response.ok) return [];
    const rows = (await response.json()) as T[];
    return Array.isArray(rows) ? rows.filter(predicate).map((row) => row.slug) : [];
  } catch {
    return [];
  }
}

/** Wire shape of a `GET /api/fonts` row (SPEC F104.7) — only the one field this page reads; mirrors
 * `wardrobe/page.tsx`'s own local `SettingRow` idiom (cast to the one field a caller needs rather
 * than importing the Wardrobe page's full `FontLibraryPackDto` for a single string). */
interface InstalledFontPackRow {
  slug: string;
}

/**
 * Every already-installed pack's slug (PLAN T204, Dean's post-v3.1.0 review: reopening an installed
 * pack's detail panel showed no sign it was already installed) — fetched ALONGSIDE the index below,
 * in the SAME server component, the smaller diff over a lazy per-open client fetch (the alternative
 * this task's own dispatch note weighed): one more `Promise.all` leg here, versus a second
 * client-side fetch/loading state threaded through `PersonaCatalogClient.loadDetail` for font
 * entries only. Independent of `Community:CatalogIndexUrl` (SPEC F104.8's offline floor — an
 * installed pack outlives the catalog, the same reasoning `wardrobe/page.tsx`'s own ungated nav item
 * follows) — fetched unconditionally, never gated on the catalog being enabled. Every row counts
 * (no predicate): a font pack has only one provenance path (SPEC F104.5), so "installed" and
 * "genuinely installed by this route" are the same thing.
 */
async function fetchInstalledFontSlugs(cookieHeader: string): Promise<string[]> {
  return fetchSlugs<InstalledFontPackRow>("/api/fonts", cookieHeader);
}

/** Wire shape of one `Station:Theme` choice, off `GET /api/settings` (SPEC F103.11, PLAN T187) —
 * only the fields this page reads; mirrors `InstalledFontPackRow`'s own narrow-cast idiom above
 * rather than importing `settings/settings-types.ts`'s full `SettingChoice` for three fields. */
interface StationThemeChoiceRow {
  value: string;
  importedFrom?: string | null;
  importedAt?: string | null;
}

/** Wire shape of one `GET /api/settings` row — only the one field this page reads (mirrors
 * `wardrobe/page.tsx`'s own local `SettingRow` idiom for the SAME endpoint, a different key). */
interface SettingRow {
  key: string;
  choices?: StationThemeChoiceRow[];
}

const STATION_THEME_KEY = "Station:Theme";

/** Wire shape of one `GET /api/shows` row — only the fields this page reads (mirrors
 * `InstalledFontPackRow`'s own narrow-cast idiom above). `importedFrom` mirrors `GenWave.Host.Api.ShowDto`'s
 * own field — `null` for an AUTHORED row, non-null for a genuinely imported one (SPEC F115.5's own
 * two-provenance-class rule) — see `fetchImportedShowSlugs`'s own remarks for why this page reads it. */
interface ShowRow {
  slug: string;
  importedFrom: string | null;
}

/**
 * Every catalog slug ALREADY GENUINELY IMPORTED as a local show (SPEC F118.1, PLAN T255) — the show
 * half of `installedFontSlugs`'s own "reopening shows no installed state" fix above (gh-#375),
 * fetched ALONGSIDE the index in the SAME server component. `GET /api/shows` is unconditional —
 * this page's own admin surface, not gated on `Community:CatalogIndexUrl` — mirroring
 * `fetchInstalledFontSlugs`'s own "independent of the catalog itself" posture.
 *
 * <b>`row.importedFrom !== null` (PLAN T255 review finding F2, MEDIUM) — mirrors
 * `fetchInstalledThemeProvenance`'s own `importedFrom != null` filter below, applied to shows'
 * OWN two-provenance-class rule (SPEC F115.5): a show slug can ALSO be taken by an AUTHORED row
 * that merely collides with a catalog entry's slug — that row's `importedFrom` is `null`.</b>
 * Admitting it here (the pre-fix shape: every row's slug, unconditionally) made
 * `ShowShelfCard`/`ShowCardReviewModal` render an "Imported"/"Confirm re-import" state for a slug
 * that was never actually imported — a re-import attempt would then 409 (SPEC F115.5's own
 * authored-slug-reserved rule) against a UI that had just told the operator it would succeed.
 * Filtering here means an authored-colliding slug renders the SAME "never seen this before" state a
 * genuinely-unclaimed slug does (no chip, "Confirm import") — honest, and the eventual 409 (should
 * the operator still confirm) already surfaces through `ShowCardReviewModal`'s own generic
 * `readErrorMessage` failure path, same as any other refused import.
 */
async function fetchImportedShowSlugs(cookieHeader: string): Promise<string[]> {
  return fetchSlugs<ShowRow>("/api/shows", cookieHeader, (row) => row.importedFrom !== null);
}

/** Wire shape of one `GET /api/personas` row — only the one field this page reads (mirrors
 * `ShowRow`'s own narrow-cast idiom immediately above, a different endpoint). */
interface PersonaRow {
  slug: string;
}

/**
 * Every local persona's own slug (SPEC F118.3, PLAN T255) — the "not already hired" half of the
 * soft "also hire ⟨persona⟩" offer's eligibility gate (`PersonaCatalogClient`'s own remarks name
 * the other, on-shelf half). Fetched ALONGSIDE the index in the SAME server component. Every row
 * counts (no predicate, unlike `fetchImportedShowSlugs` above): "hired" has no authored-collision
 * ambiguity the way a show slug does — any local persona row, imported or authored, is someone the
 * offer should never re-suggest hiring. Note the DIRECTION of a fetch failure here: degrading to
 * `[]` means "assume nobody is hired yet", which can only ever WITHHOLD an offer that should have
 * appeared, never wrongly claim a persona is already hired — the safer default (a withheld
 * convenience over a confusing "hire" attempt on someone the operator already has), the same
 * reasoning `PersonaCatalogClient`'s own prop remarks give in full.
 */
async function fetchHiredPersonaSlugs(cookieHeader: string): Promise<string[]> {
  return fetchSlugs<PersonaRow>("/api/personas", cookieHeader);
}

/**
 * Every catalog-imported theme's provenance (gh-#375, Dean's demo feedback — the theme
 * half of the font half's own T204 "reopening an installed pack shows no installed state" fix).
 *
 * <b>Route choice (this task's own dispatch note): `GET /api/settings`, not a new `GET /api/themes`
 * route.</b> `Station:Theme`'s choices already widen to shipped ∪ owner themes with
 * `importedFrom`/`importedAt` per choice (SPEC F103.7/F103.11, PLAN T183/T187,
 * `StationSettingsAllowlist.ThemeChoices`/`SettingChoice`) — the exact data this page needs already
 * rides an existing, generic endpoint, so a dedicated `GET /api/themes` listing (the font half's own
 * `/api/fonts` shape) would duplicate that projection for zero new capability. The one thing this
 * costs over a dedicated route: this page reads the WHOLE settings document (every allowlisted key,
 * not only `Station:Theme`) to reach one field. That document is small (one row per allowlisted
 * key, no large payloads — SPEC F55.3's full-coverage allowlist is still a few dozen rows) and
 * already fetched wholesale by other authed pages for a single key each (`wardrobe/page.tsx`'s own
 * `fetchCatalogEnabled`, `layout.tsx`'s own `Station:Theme` read for the header's ThemeSwitcher) —
 * this is that same established shape, not a new pattern.
 *
 * Fetched ALONGSIDE the index and `installedFontSlugs`, in the SAME server component (one more
 * `Promise.all` leg, the smaller diff over a lazy per-open client fetch). Any failure (network
 * error, non-200, an unexpected shape) degrades to `[]` — fail closed, matching
 * `fetchInstalledFontSlugs`'s own posture above: no live signal means no theme gets FALSELY claimed
 * installed.
 */
async function fetchInstalledThemeProvenance(cookieHeader: string): Promise<ThemeCatalogProvenanceDto[]> {
  try {
    const response = await apiGet("/api/settings", { cookies: cookieHeader });
    if (!response.ok) return [];
    const settings = (await response.json()) as SettingRow[];
    const themeSetting = settings.find((row) => row.key === STATION_THEME_KEY);
    const choices = themeSetting?.choices ?? [];
    return choices
      .filter(
        (choice): choice is StationThemeChoiceRow & { importedFrom: string; importedAt: string } =>
          choice.importedFrom != null && choice.importedAt != null
      )
      .map((choice) => ({ slug: choice.value, importedFrom: choice.importedFrom, importedAt: choice.importedAt }));
  } catch {
    return [];
  }
}

interface PersonaCatalogPageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

export default async function PersonaCatalogPage({ searchParams }: PersonaCatalogPageProps): Promise<ReactNode> {
  const sp = await searchParams;
  // gh-#372 — which kind tab this render shows; anything unrecognised falls back to personas.
  const activeKind = resolveCatalogKind(sp.kind);
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();
  const [response, installedFontSlugs, installedThemeProvenance, importedShowSlugs, hiredPersonaSlugs] =
    await Promise.all([
      apiGet("/api/catalog/index", { cookies: cookieHeader }),
      fetchInstalledFontSlugs(cookieHeader),
      fetchInstalledThemeProvenance(cookieHeader),
      fetchImportedShowSlugs(cookieHeader),
      fetchHiredPersonaSlugs(cookieHeader),
    ]);

  // Disabled (SPEC F90.1): CatalogController serves a bare, zero-byte 404 here — the same
  // per-resource 404 shape MediaDetailPage's own "Not found" branch renders inline for (house
  // pattern, catalog/[mediaId]/page.tsx), rather than Next's framework notFound() (unused
  // anywhere else in this codebase). The Persona Catalog nav entry is ALSO hidden on this same
  // signal (see the authed layout) — this inline render only covers a direct/bookmarked visit.
  if (response.status === 404) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Not found</h1>
        <p className="mt-2 text-[0.85rem] text-mute">This page isn&apos;t available.</p>
      </main>
    );
  }

  if (!response.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Community Catalog</h1>
        <p className="mt-4 text-[0.85rem] text-danger">Unable to load the community catalog.</p>
      </main>
    );
  }

  const index = (await response.json()) as CatalogIndexResponseDto;

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Community Catalog</h1>
      <div className="mt-4">
        <PersonaCatalogTabs activeKind={activeKind} />
      </div>
      <div className="mt-6">
        <PersonaCatalogClient
          initialIndex={index}
          installedFontSlugs={installedFontSlugs}
          installedThemeProvenance={installedThemeProvenance}
          importedShowSlugs={importedShowSlugs}
          hiredPersonaSlugs={hiredPersonaSlugs}
          activeKind={activeKind}
        />
      </div>
    </main>
  );
}
