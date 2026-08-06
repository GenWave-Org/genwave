import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { FontLibraryClient } from "./FontLibraryClient";
import type { FontLibraryPackDto } from "./types";

// A pack can be installed or (later, M2) uninstalled at any time from elsewhere in the app —
// always re-render from the server, mirroring personas/page.tsx and persona-catalog/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

/** Wire shape of a `GET /api/settings` row — only the one field this page reads. */
interface SettingRow {
  key: string;
  value: string;
}

/** The one F19 allowlist key this page reads to pick the empty-state CTA (SPEC F90.1) — mirrors the
 * authed layout's own `CATALOG_INDEX_URL_KEY` (`app/(authed)/layout.tsx`'s `fetchSettingsSnapshot`). */
const CATALOG_INDEX_URL_KEY = "Community:CatalogIndexUrl";

/**
 * Whether the Community Catalog is enabled (SPEC F90.1) — the SAME `Community:CatalogIndexUrl`
 * non-empty signal the authed layout derives for the Sidebar/MobileNav nav-gate, re-derived here off
 * this page's own independent `GET /api/settings` read (this page and the layout each own their own
 * read — the layout's folds `Station:Theme` in for the header's ThemeSwitcher, a combination this
 * page has no use for; mirrors how every other authed page, e.g. persona-catalog/page.tsx, fetches
 * what it needs on its own rather than threading props down from the layout, which the App Router
 * gives no channel for). Threads into `FontLibraryClient`'s empty-state CTA (PLAN T203 review finding
 * F3) so a disabled catalog swaps "browse the catalog" for a pointer at Settings instead of linking
 * to `/persona-catalog`, which itself 404s off-catalog — the exact dead end the Library nav item's
 * own deliberate ungating (SPEC F104.8) exists to let an operator avoid. Any failure (network error,
 * non-200) degrades to `false` — fail closed, matching F90.1's own posture.
 */
async function fetchCatalogEnabled(cookieHeader: string): Promise<boolean> {
  try {
    const response = await apiGet("/api/settings", { cookies: cookieHeader });
    if (!response.ok) return false;
    const settings = (await response.json()) as SettingRow[];
    const catalogRow = settings.find((row) => row.key === CATALOG_INDEX_URL_KEY);
    return catalogRow !== undefined && catalogRow.value.trim() !== "";
  } catch {
    return false;
  }
}

export default async function LibraryPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();
  const [response, catalogEnabled] = await Promise.all([
    apiGet("/api/fonts", { cookies: cookieHeader }),
    fetchCatalogEnabled(cookieHeader),
  ]);

  if (!response.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Library</h1>
        <p className="mt-4 text-[0.85rem] text-danger">Unable to load the installed font library.</p>
      </main>
    );
  }

  const packs = (await response.json()) as FontLibraryPackDto[];

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Library</h1>
      <div className="mt-4">
        <FontLibraryClient packs={packs} catalogEnabled={catalogEnabled} />
      </div>
    </main>
  );
}
