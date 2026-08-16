import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { AvatarWardrobeClient } from "./AvatarWardrobeClient";
import { IconWardrobeClient } from "./IconWardrobeClient";
import { InstalledEntriesList } from "./InstalledEntriesList";
import { WardrobeClient } from "./WardrobeClient";
import { resolveWardrobeTab, WardrobeTabs, type WardrobeTab } from "./WardrobeTabs";
import type { AvatarPackSummaryDto, FontLibraryPackDto, IconPackSummaryDto, InstalledEntryRow } from "./types";

// A pack can be installed elsewhere in the app, or uninstalled right here (gh-#428,
// UninstallPackButton's own router.refresh() call re-triggers this exact server render) — always
// re-render from the server, mirroring personas/page.tsx and persona-catalog/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

interface WardrobePageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

/** Wire shape of one `Station:Theme` choice off `GET /api/settings` — only the fields this page
 * reads (the `persona-catalog/page.tsx` narrow-cast idiom for the SAME derivation, plus `label`
 * for the card title). */
interface StationThemeChoiceRow {
  value: string;
  label: string;
  importedFrom?: string | null;
  importedAt?: string | null;
}

/** Wire shape of a `GET /api/settings` row — only the fields this page reads. */
interface SettingRow {
  key: string;
  value?: string;
  choices?: StationThemeChoiceRow[];
}

/** Wire shape of one `GET /api/personas` row — only the fields this page reads. */
interface PersonaRow {
  slug: string;
  name: string;
  importedFrom: string | null;
  importedAt: string | null;
}

/** Wire shape of one `GET /api/shows` row — only the fields this page reads. */
interface ShowRow {
  slug: string;
  name: string;
  tagline: string | null;
  importedFrom: string | null;
  importedAt: string | null;
}

/** The F19 allowlist key whose non-emptiness = "the catalog is enabled" (SPEC F90.1) — mirrors the
 * authed layout's own `CATALOG_INDEX_URL_KEY` (`app/(authed)/layout.tsx`'s `fetchSettingsSnapshot`). */
const CATALOG_INDEX_URL_KEY = "Community:CatalogIndexUrl";

const STATION_THEME_KEY = "Station:Theme";

/** `Station:IconPack`'s own key (SPEC F130.4) — mirrors `STATION_THEME_KEY` immediately above, a
 * different allowlisted Choice setting. */
const STATION_ICON_PACK_KEY = "Station:IconPack";

/**
 * Everything this page reads off `GET /api/settings`, in ONE request (gh-#393, widened at PLAN
 * T304): the catalog-enabled signal (the empty-state CTA swap, PLAN T203 review finding F3 — see
 * `WardrobeClient`'s own `catalogEnabled` remarks for the full posture), the Themes tab's rows —
 * every `Station:Theme` choice carrying the F103.11 provenance pair, the SAME settings-derived
 * derivation `persona-catalog/page.tsx`'s own `fetchInstalledThemeProvenance` documents at length
 * (no `GET /api/themes` round trip for data an existing endpoint already carries) — AND the Icons
 * tab's own active-pack slug (`Station:IconPack`'s current `value`, SPEC F130.4), the SAME
 * settings-derived signal `IconWardrobeClient`'s own "Active" chip and fail-open uninstall copy
 * need. `null` themes = the fetch itself failed (the tab renders its load error); catalog-enabled
 * and `stationIconPackSlug` both degrade to their own "no live signal" defaults (`false`/`""`) —
 * fail closed, matching F90.1's own posture.
 */
async function fetchSettingsFacts(
  cookieHeader: string
): Promise<{ catalogEnabled: boolean; themes: InstalledEntryRow[] | null; stationIconPackSlug: string }> {
  try {
    const response = await apiGet("/api/settings", { cookies: cookieHeader });
    if (!response.ok) return { catalogEnabled: false, themes: null, stationIconPackSlug: "" };
    const settings = (await response.json()) as SettingRow[];

    const catalogRow = settings.find((row) => row.key === CATALOG_INDEX_URL_KEY);
    const catalogEnabled = catalogRow?.value !== undefined && catalogRow.value.trim() !== "";

    const choices = settings.find((row) => row.key === STATION_THEME_KEY)?.choices ?? [];
    const themes = choices
      .filter(
        (choice): choice is StationThemeChoiceRow & { importedFrom: string; importedAt: string } =>
          choice.importedFrom != null && choice.importedAt != null
      )
      .map((choice) => ({
        slug: choice.value,
        name: choice.label,
        detail: null,
        importedFrom: choice.importedFrom,
        importedAt: choice.importedAt,
      }));

    const stationIconPackSlug = settings.find((row) => row.key === STATION_ICON_PACK_KEY)?.value ?? "";

    return { catalogEnabled, themes, stationIconPackSlug };
  } catch {
    return { catalogEnabled: false, themes: null, stationIconPackSlug: "" };
  }
}

/**
 * One kind's listing → its genuinely-imported `InstalledEntryRow`s (gh-#393). `importedFrom !=
 * null` is the two-provenance-class rule (`persona-catalog/page.tsx`'s own T255/F2 reasoning): an
 * authored persona/show is not wardrobe content — it never came off the shelf. `null` (any fetch
 * failure or a non-array body) is DISTINCT from `[]` here, unlike that page's degrade-to-`[]`
 * fetchers: there a lost signal only withholds a decorative chip, here it IS the tab's content — a
 * silent `[]` would render "nothing installed" as a fact this page doesn't actually know.
 */
async function fetchImportedRows<T extends { slug: string; name: string; importedFrom: string | null; importedAt: string | null }>(
  path: string,
  cookieHeader: string,
  project: (row: T & { importedFrom: string; importedAt: string }) => InstalledEntryRow
): Promise<InstalledEntryRow[] | null> {
  try {
    const response = await apiGet(path, { cookies: cookieHeader });
    if (!response.ok) return null;
    const rows = (await response.json()) as T[];
    if (!Array.isArray(rows)) return null;
    return rows
      .filter((row): row is T & { importedFrom: string; importedAt: string } => row.importedFrom != null && row.importedAt != null)
      .map(project);
  } catch {
    return null;
  }
}

async function fetchFontPacks(cookieHeader: string): Promise<FontLibraryPackDto[] | null> {
  try {
    const response = await apiGet("/api/fonts", { cookies: cookieHeader });
    if (!response.ok) return null;
    return (await response.json()) as FontLibraryPackDto[];
  } catch {
    return null;
  }
}

/** Every installed avatar pack (SPEC F128.3, PLAN T294) — mirrors `fetchFontPacks`'s own shape
 * verbatim for the SAME reasoning, a different endpoint (`GET /api/avatar-packs`, this task's own
 * minimal listing route). `null` (any fetch failure or non-200) is distinct from `[]` here, unlike
 * the shelf page's own degrade-to-`[]` fetchers — see `fetchImportedRows`'s own remarks for why: a
 * lost signal here IS the tab's content, a silent `[]` would render "nothing installed" as a fact
 * this page doesn't actually know. */
async function fetchAvatarPacks(cookieHeader: string): Promise<AvatarPackSummaryDto[] | null> {
  try {
    const response = await apiGet("/api/avatar-packs", { cookies: cookieHeader });
    if (!response.ok) return null;
    return (await response.json()) as AvatarPackSummaryDto[];
  } catch {
    return null;
  }
}

/** Every installed icon pack (SPEC F130.4, PLAN T304) — mirrors `fetchAvatarPacks`'s own shape
 * verbatim, a different endpoint (`GET /api/icon-packs`). `null` (any fetch failure or non-200) is
 * distinct from `[]` here, the SAME "a lost signal IS the tab's content" reasoning every other
 * installed-listing fetcher on this page follows. */
async function fetchIconPacks(cookieHeader: string): Promise<IconPackSummaryDto[] | null> {
  try {
    const response = await apiGet("/api/icon-packs", { cookies: cookieHeader });
    if (!response.ok) return null;
    return (await response.json()) as IconPackSummaryDto[];
  } catch {
    return null;
  }
}

/** The active tab's own load-failure line — the `catalog/page.tsx` "Unable to load …" shape; every
 * OTHER tab stays reachable through the strip, so one wedged endpoint never blanks the whole page. */
function loadError(what: string): ReactNode {
  return <p className="text-[0.85rem] text-danger">Unable to load {what}.</p>;
}

/**
 * The Wardrobe (SPEC F104.7, widened by gh-#393, PLAN T294's Avatars tab, and PLAN T304's Icons
 * tab): everything installed off the Community Catalog, siloed by kind — Personas | Themes | Fonts
 * | Shows | Avatars | Icons, one tab each (URL-driven, the CatalogTabs idiom), every tab present
 * even when empty (Dean's ruling on the issue). Fonts keep their original `WardrobeClient` cards
 * (faces, licence, uninstall — this page's founding kind); Personas/Themes/Shows render the shared
 * read-only `InstalledEntriesList`; Avatars gets its own `AvatarWardrobeClient` (an item grid +
 * uninstall); Icons gets its own `IconWardrobeClient` (a specimen row drawn by the safe renderer +
 * uninstall, mirroring `AvatarWardrobeClient`'s own shape one kind over). All six sources fetch in
 * one `Promise.all` alongside the settings read that serves the catalog-enabled signal, the Themes
 * rows, AND the Icons tab's own active-pack slug.
 */
export default async function WardrobePage({ searchParams }: WardrobePageProps): Promise<ReactNode> {
  const sp = await searchParams;
  const activeTab: WardrobeTab = resolveWardrobeTab(sp.tab);
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  const [packs, settingsFacts, personas, shows, avatarPacks, iconPacks] = await Promise.all([
    fetchFontPacks(cookieHeader),
    fetchSettingsFacts(cookieHeader),
    fetchImportedRows<PersonaRow>("/api/personas", cookieHeader, (row) => ({
      slug: row.slug,
      name: row.name,
      detail: null,
      importedFrom: row.importedFrom,
      importedAt: row.importedAt,
    })),
    fetchImportedRows<ShowRow>("/api/shows", cookieHeader, (row) => ({
      slug: row.slug,
      name: row.name,
      detail: row.tagline,
      importedFrom: row.importedFrom,
      importedAt: row.importedAt,
    })),
    fetchAvatarPacks(cookieHeader),
    fetchIconPacks(cookieHeader),
  ]);
  const { catalogEnabled, themes, stationIconPackSlug } = settingsFacts;

  let tabContent: ReactNode;
  switch (activeTab) {
    case "personas":
      tabContent =
        personas === null ? (
          loadError("hired personas")
        ) : (
          <InstalledEntriesList
            rows={personas}
            ariaLabel="Hired personas"
            provenanceVerb="Hired"
            emptyTitle="No personas hired"
            emptyReason="Browse the Community Catalog to hire a DJ for this station."
            catalogEnabled={catalogEnabled}
          />
        );
      break;
    case "themes":
      tabContent =
        themes === null ? (
          loadError("installed themes")
        ) : (
          <InstalledEntriesList
            rows={themes}
            ariaLabel="Installed themes"
            provenanceVerb="Imported"
            emptyTitle="No themes installed"
            emptyReason="Browse the Community Catalog to install a theme for this station."
            catalogEnabled={catalogEnabled}
          />
        );
      break;
    case "fonts":
      tabContent =
        packs === null ? (
          loadError("the installed font wardrobe")
        ) : (
          <WardrobeClient packs={packs} catalogEnabled={catalogEnabled} />
        );
      break;
    case "shows":
      tabContent =
        shows === null ? (
          loadError("imported shows")
        ) : (
          <InstalledEntriesList
            rows={shows}
            ariaLabel="Imported shows"
            provenanceVerb="Imported"
            emptyTitle="No shows imported"
            emptyReason="Browse the Community Catalog to import a show for this station."
            catalogEnabled={catalogEnabled}
          />
        );
      break;
    case "avatars":
      tabContent =
        avatarPacks === null ? (
          loadError("the installed avatar packs")
        ) : (
          <AvatarWardrobeClient packs={avatarPacks} catalogEnabled={catalogEnabled} />
        );
      break;
    case "icons":
      tabContent =
        iconPacks === null ? (
          loadError("the installed icon packs")
        ) : (
          <IconWardrobeClient packs={iconPacks} catalogEnabled={catalogEnabled} activeSlug={stationIconPackSlug} />
        );
      break;
  }

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Wardrobe</h1>
      <div className="mt-4">
        <WardrobeTabs activeTab={activeTab} />
      </div>
      <div className="mt-6">{tabContent}</div>
    </main>
  );
}
