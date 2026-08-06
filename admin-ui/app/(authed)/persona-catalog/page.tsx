import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { PersonaCatalogClient } from "./PersonaCatalogClient";
import type { CatalogIndexResponseDto } from "./types";

// The underlying Community:CatalogIndexUrl setting is live-editable from Settings (SPEC F90.1),
// and the shelf itself can flip disabled<->enabled or gain/lose entries between visits — always
// re-render from the server, mirroring personas/page.tsx and safe-content/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

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
 * follows) — fetched unconditionally, never gated on the catalog being enabled. Any failure
 * (network error, non-200, or an unexpected non-array body) degrades to `[]` — fail closed, matching
 * `fetchCatalogEnabled`'s own posture below: no live signal means no pack gets FALSELY claimed
 * installed.
 */
async function fetchInstalledFontSlugs(cookieHeader: string): Promise<string[]> {
  try {
    const response = await apiGet("/api/fonts", { cookies: cookieHeader });
    if (!response.ok) return [];
    const rows = (await response.json()) as InstalledFontPackRow[];
    return Array.isArray(rows) ? rows.map((row) => row.slug) : [];
  } catch {
    return [];
  }
}

export default async function PersonaCatalogPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();
  const [response, installedFontSlugs] = await Promise.all([
    apiGet("/api/catalog/index", { cookies: cookieHeader }),
    fetchInstalledFontSlugs(cookieHeader),
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
        <PersonaCatalogClient initialIndex={index} installedFontSlugs={installedFontSlugs} />
      </div>
    </main>
  );
}
