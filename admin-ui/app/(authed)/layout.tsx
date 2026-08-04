import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import type { ThemeChoice } from "@/lib/theme";
import { BreadcrumbTitleProvider } from "./_components/BreadcrumbTitle";
import { Breadcrumbs } from "./_components/Breadcrumbs";
import { MobileNav } from "./_components/MobileNav";
import { Sidebar } from "./_components/Sidebar";
import { ThemeSwitcher } from "./_components/ThemeSwitcher";
import { Toaster } from "@/components/ui/toast";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";

interface AuthedLayoutProps {
  children: ReactNode;
}

const FALLBACK_STATION_NAME = "GenWave";

/** Wire shape of a `GET /api/stations` list item (see Host's StationDto). */
interface StationDto {
  id: number;
  name: string;
}

/** The one F19 allowlist key this layout reads to know whether to list the Persona Catalog nav
 * entry (SPEC F90.1) — mirrors `PersonasPage`'s own single-key read off `GET /api/settings`. */
const CATALOG_INDEX_URL_KEY = "Community:CatalogIndexUrl";

/** The `Station:Theme` setting key (SPEC F102.12/F102.13, PLAN T167) — its `choices` are the
 * SAME closed set `GET /api/settings` already carries for the Settings-page's `ChoiceSettingControl`
 * (T175); this layout reads the identical row for the header's `ThemeSwitcher` rather than adding
 * a second endpoint (the /design ruling, 2026-08-04). */
const THEME_SETTING_KEY = "Station:Theme";

/** Shape of a `GET /api/settings` row — only the fields this layout reads. `choices` is present
 * only for `Station:Theme` (a `kind === "choice"` row); every other row this layout scans leaves
 * it `undefined`, which `fetchSettingsSnapshot` treats as "no theme choices on this row". */
interface SettingRow {
  key: string;
  value: string;
  choices?: ThemeChoice[];
}

/** Everything this layout's header/nav chrome derives from `GET /api/settings` (SPEC F90.1,
 * F102.12/F102.13, PLAN T167). */
interface SettingsSnapshot {
  catalogEnabled: boolean;
  themeChoices: readonly ThemeChoice[];
  stationThemeSlug: string;
}

const EMPTY_SETTINGS_SNAPSHOT: SettingsSnapshot = {
  catalogEnabled: false,
  themeChoices: [],
  stationThemeSlug: "",
};

/**
 * The ONE `GET /api/settings` read this layout needs for two otherwise-unrelated pieces of chrome
 * — the /design ruling (SPEC F102.12, 2026-08-04): the admin theme switcher sources its list from
 * this SAME settings response, never a second/templated endpoint, symmetric with how the
 * spectator surface's switcher (T166) reads its own list. A future settings-derived chrome flag
 * should extend this function's derivation, not add a second identical `/api/settings` fetch
 * beside it.
 *
 * - `catalogEnabled`: `Community:CatalogIndexUrl` non-empty is the SAME fail-closed signal
 *   `CommunityCatalogAccessor.IsEnabled` uses server-side for the actual `/api/catalog/*` routes
 *   (SPEC F90.1, PLAN T102) — read here via the settings surface rather than probing
 *   `/api/catalog/index` itself, which would mean every navigation on every page pays for a live
 *   upstream catalog fetch just to decide whether to show a sidebar link.
 * - `themeChoices`/`stationThemeSlug`: the `Station:Theme` row's closed set and current value
 *   (`SettingDto.choices`/`.value`) — passed straight to `ThemeSwitcher`, which resolves the
 *   visitor's actual pre-selection (cookie > station value > `isDefault` choice) client-side.
 *
 * Any failure (network error, non-200) degrades to {@link EMPTY_SETTINGS_SNAPSHOT} — fail closed
 * for the catalog flag (matching F90.1's own posture); the header's ThemeSwitcher renders with no
 * theme choices in that case (mode toggle still works — see its own remarks).
 */
async function fetchSettingsSnapshot(cookieHeader: string): Promise<SettingsSnapshot> {
  try {
    const response = await apiGet("/api/settings", { cookies: cookieHeader });
    if (!response.ok) return EMPTY_SETTINGS_SNAPSHOT;
    const settings = (await response.json()) as SettingRow[];

    const catalogRow = settings.find((s) => s.key === CATALOG_INDEX_URL_KEY);
    const catalogEnabled = catalogRow !== undefined && catalogRow.value.trim() !== "";

    const themeRow = settings.find((s) => s.key === THEME_SETTING_KEY);
    return {
      catalogEnabled,
      themeChoices: themeRow?.choices ?? [],
      stationThemeSlug: themeRow?.value ?? "",
    };
  } catch {
    return EMPTY_SETTINGS_SNAPSHOT;
  }
}

/**
 * Reads the live station name for the shell wordmark (SPEC F44.7, closes gitea-#195).
 * Falls back to the "GenWave" product brand on any failure — non-200, a
 * network error, or an empty station list — so the shell chrome never
 * renders blank or throws. `GET /api/stations` reads the live-effective name
 * on every call (post-V7), so a `Station:Name` settings edit shows up on the
 * shell's very next navigation with no client polling.
 */
async function fetchStationName(cookieHeader: string): Promise<string> {
  try {
    const response = await apiGet("/api/stations", { cookies: cookieHeader });
    if (!response.ok) {
      return FALLBACK_STATION_NAME;
    }
    const stations = (await response.json()) as StationDto[];
    return stations[0]?.name ?? FALLBACK_STATION_NAME;
  } catch {
    return FALLBACK_STATION_NAME;
  }
}

// Persistent shell for every authenticated route (SPEC F28.5). Auth itself is
// already enforced by middleware.ts on these paths — this layout only adds
// the chrome (sidebar, breadcrumb slot, theme switcher) around whatever the
// route renders; it does not re-check the session. Feedback primitives
// (SPEC F28.9/F28.14) mount here once: the toast viewport lives at the shell
// level, and ConfirmDialogProvider wraps the routed content so any page can
// call useConfirm() without re-mounting the dialog per page.
// BreadcrumbTitleProvider wraps both the header (Breadcrumbs) and the routed
// content — a nested page several levels below the header still needs to
// reach up and set the trailing crumb (STORY-090 AC4).
//
// Responsive shell (SPEC F28.13, STORY-093): Sidebar renders persistently
// but is `hidden` below 1024px (Tailwind's `lg:` breakpoint); MobileNav's
// hamburger — visible only below 1024px — opens the same nav as a
// focus-trapped Radix Dialog drawer instead. `min-w-0` on both the content
// column and <main> keeps a wide, unwrapped descendant from ever forcing the
// page body itself to scroll sideways — individual wide tables opt into
// their own `overflow-x-auto` container instead (AC2).
export default async function AuthedLayout({ children }: AuthedLayoutProps): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();
  const [stationName, settingsSnapshot] = await Promise.all([
    fetchStationName(cookieHeader),
    fetchSettingsSnapshot(cookieHeader),
  ]);
  const { catalogEnabled, themeChoices, stationThemeSlug } = settingsSnapshot;

  return (
    <BreadcrumbTitleProvider>
      <div className="flex min-h-screen bg-bg text-ink">
        <Sidebar stationName={stationName} catalogEnabled={catalogEnabled} />
        <div className="flex min-w-0 flex-1 flex-col">
          <header className="flex h-14 shrink-0 items-center justify-between gap-3 border-b border-line bg-surface px-4 sm:px-6">
            <div className="flex min-w-0 items-center gap-3">
              <MobileNav stationName={stationName} catalogEnabled={catalogEnabled} />
              <Breadcrumbs />
            </div>
            <ThemeSwitcher choices={themeChoices} stationThemeSlug={stationThemeSlug} />
          </header>
          <main className="min-w-0 flex-1 p-4 sm:p-6">
            <ConfirmDialogProvider>{children}</ConfirmDialogProvider>
          </main>
        </div>
        <Toaster />
      </div>
    </BreadcrumbTitleProvider>
  );
}
