import type { Metadata } from "next";
import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import type { ThemeChoice } from "@/lib/theme";
import { BreadcrumbTitleProvider } from "./_components/BreadcrumbTitle";
import { Breadcrumbs } from "./_components/Breadcrumbs";
import { IconPackProvider } from "./_components/IconPackContext";
import { MobileNav } from "./_components/MobileNav";
import { Sidebar } from "./_components/Sidebar";
import { ThemeSwitcher } from "./_components/ThemeSwitcher";
import { Toaster } from "@/components/ui/toast";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";

interface AuthedLayoutProps {
  children: ReactNode;
}

const FALLBACK_STATION_NAME = "GenWave";

/** Wire shape of a `GET /api/stations` list item (see Host's StationDto). `stationImageToken` is
 * `null` when the station has never customized its image (SPEC F131.3, PLAN T307 fix round) — a
 * bytes-free `IStationImageStore.GetTokenAsync` read folded into this SAME row, never a second
 * per-navigation fetch of the image's own bytes. */
interface StationDto {
  id: number;
  name: string;
  stationImageToken: string | null;
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
 * The active icon pack's own raw canonical JSON text (SPEC F130.3/F130.4, STORY-337, PLAN T304
 * rider 6) — the layout-snapshot fold the T303 review recommended over a per-page client fetch of
 * `GET /api/icon-packs/active` (that route carries no ETag/cache validator of its own): this
 * server-side read happens ONCE per authed navigation, alongside `fetchSettingsSnapshot`'s own
 * single `GET /api/settings` read, in the SAME `Promise.all` below — mirroring exactly how
 * `stationThemeSlug`/`themeChoices` already ride this layout rather than `ThemeSwitcher` fetching
 * its own list. `200` carries the definition body as plain text (parsed defensively, client-side,
 * by `IconPackProvider` — never trusted here even though this station's own `Active` route already
 * re-validates before serving); `204` (`Station:IconPack` unset, or the F130.5 fail-open uninstall)
 * and any failure alike degrade to `null` — house icons, never an error.
 */
async function fetchActiveIconPackDefinitionText(cookieHeader: string): Promise<string | null> {
  try {
    const response = await apiGet("/api/icon-packs/active", { cookies: cookieHeader });
    if (response.status !== 200) return null;
    return await response.text();
  } catch {
    return null;
  }
}

/** Everything this layout's chrome derives from `GET /api/stations` — the wordmark and the tab-icon
 * token alike (SPEC F44.7/F131.3, PLAN T307 fix round). */
interface StationSnapshot {
  name: string;
  stationImageToken: string | null;
}

const EMPTY_STATION_SNAPSHOT: StationSnapshot = {
  name: FALLBACK_STATION_NAME,
  stationImageToken: null,
};

/**
 * The ONE `GET /api/stations` read this layout needs for two otherwise-unrelated pieces of chrome —
 * the shell wordmark (SPEC F44.7, closes gitea-#195) AND the authed tab-icon's own token (SPEC
 * F131.3, PLAN T307 fix round). Falls back to {@link EMPTY_STATION_SNAPSHOT} on any failure —
 * non-200, a network error, or an empty station list — so the shell chrome never renders blank or
 * throws, and the tab icon stays the shipped one. `GET /api/stations` reads both fields
 * live-effective on every call (the name since V7, the token via a bytes-free
 * `IStationImageStore.GetTokenAsync` read added at T307) — a `Station:Name` edit or a station-image
 * upload/delete alike show up on the very next navigation, no client polling, no api restart.
 *
 * {@link generateMetadata} below calls this SAME function a second time rather than sharing this
 * call's own result — a separate Next.js export can't read another export's local variables, and
 * `apiGet`'s `no-store` cache option means Next's request-memoization wouldn't dedupe the two calls
 * even if it could. The traded-off cost is one extra `GET /api/stations` round trip per authed
 * navigation; the traded-off WIN is what this fix round exists for: neither call ever touches the
 * station image's own ≤512 KiB bytes column, unlike the per-navigation `GET /api/station/image`
 * probe this function replaces.
 */
async function fetchStationSnapshot(cookieHeader: string): Promise<StationSnapshot> {
  try {
    const response = await apiGet("/api/stations", { cookies: cookieHeader });
    if (!response.ok) return EMPTY_STATION_SNAPSHOT;
    const stations = (await response.json()) as StationDto[];
    const station = stations[0];
    return {
      name: station?.name ?? FALLBACK_STATION_NAME,
      stationImageToken: station?.stationImageToken ?? null,
    };
  } catch {
    return EMPTY_STATION_SNAPSHOT;
  }
}

/**
 * Overrides the root layout's own file-convention favicon (`app/icon.png`) with the station's
 * customized image when one is set (SPEC F131.3's own "authenticated admin pages swap their tab
 * icon" posture) — Next's `icons` metadata field is NOT deep-merged across nested segments, so
 * returning it here REPLACES the root layout's resolved icon outright for every page under this
 * (authed) segment; the login page sits OUTSIDE this segment (no `(authed)` ancestor) and so keeps
 * the shipped icon untouched — AC4's "no anonymous byte route on the admin surface" holds
 * structurally, not by a runtime check, since this function never runs for that page at all.
 *
 * The href carries the token as a `?v=` query param (PLAN T307 fix round rider R2, the PersonaFace
 * `?v=` precedent) — `GET /api/station/image` itself ignores the query string (the route is
 * scoped by the row, not the token), but a changed token changes the URL the browser caches
 * against, so a re-upload's new bytes are never masked by a `Cache-Control: private, no-cache`
 * response the browser never bothered to revalidate. `{}` (no `icons` field) when unset lets the
 * inherited file-convention icon show through unchanged.
 */
export async function generateMetadata(): Promise<Metadata> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();
  const { stationImageToken } = await fetchStationSnapshot(cookieHeader);
  return stationImageToken
    ? { icons: { icon: `/api/station/image?v=${stationImageToken}` } }
    : {};
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
  const [stationSnapshot, settingsSnapshot, activeIconPackDefinitionText] = await Promise.all([
    fetchStationSnapshot(cookieHeader),
    fetchSettingsSnapshot(cookieHeader),
    fetchActiveIconPackDefinitionText(cookieHeader),
  ]);
  const { name: stationName } = stationSnapshot;
  const { catalogEnabled, themeChoices, stationThemeSlug } = settingsSnapshot;

  return (
    <IconPackProvider definitionText={activeIconPackDefinitionText}>
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
    </IconPackProvider>
  );
}
