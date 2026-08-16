import type { IconName } from "./Icon";

export interface NavItem {
  href: string;
  label: string;
  /** The icon-name-contract slot this nav item renders (SPEC F130.2, PLAN T304) — resolved through
   * `Icon` at render time (`Sidebar`/`MobileNav`'s own map), never a direct `icons.tsx` component
   * reference, so an installed icon pack swaps every nav glyph without either of those files
   * changing. */
  iconName: IconName;
  /**
   * True for a nav item that only exists when its own feature is enabled (PLAN T102, SPEC
   * F90.1's own "the eventual admin UI hides the Persona Catalog entry point on the same
   * [Community:CatalogIndexUrl] signal" ruling — see `CommunityCatalogAccessor`'s remarks). No
   * other nav item needs this today; every other section is always-on.
   */
  requiresCatalog?: boolean;
}

/**
 * Sidebar sections per SPEC F28.5, shared by the persistent desktop
 * `Sidebar` (≥1024px) and the `MobileNav` drawer (<1024px, SPEC F28.13) so
 * the two never drift.
 *
 * "Libraries" (plural — the MEDIA library, Q7, SPEC F28.11) is deliberately absent from this list:
 * it lives under the Catalog page's Libraries tab, and /libraries is only a redirect into that tab,
 * never its own rendered route. Do not confuse it with "Wardrobe" below — a DIFFERENT feature
 * entirely (SPEC F104.7, installed font packs; named "Library" through v3.1.0, renamed "Wardrobe" at
 * PLAN T204, Dean's ruling) that this same stale note used to read as ruling out too (PLAN T203
 * review finding, closed here): the two features share no code, and the naming collision was never
 * intentional.
 */
export const NAV_ITEMS: NavItem[] = [
  { href: "/dashboard", label: "Dashboard", iconName: "dashboard" },
  { href: "/live", label: "Live", iconName: "live" },
  { href: "/catalog", label: "Catalog", iconName: "catalog" },
  { href: "/safe-content", label: "Station Imaging", iconName: "safe-content" },
  { href: "/personas", label: "Personas", iconName: "persona" },
  { href: "/schedule", label: "Schedule", iconName: "schedule" },
  // Shows (SPEC F119.1, STORY-312, PLAN T244) sits immediately after Schedule — the format-clock
  // grid that assigns them (T243's assign-show endpoint) is the reason this entity exists at all.
  // Final placement across the whole sidebar is the Admin-UI-Polish lane's own call (ARCHITECTURE
  // TODO); this is the sensible spot until that pass reorders the shell.
  { href: "/shows", label: "Shows", iconName: "shows" },
  { href: "/persona-catalog", label: "Community Catalog", iconName: "persona-catalog", requiresCatalog: true },
  // Wardrobe (PLAN T203, SPEC F104.7; renamed from "Library" at PLAN T204, Dean's ruling — nav label
  // and route only, see this file's own class remarks) is deliberately NOT gated by
  // `requiresCatalog` — unlike the Community Catalog browse surface, an installed pack keeps serving
  // with the catalog disabled or unreachable (SPEC F104.8's offline floor), so the page that
  // inspects what's ALREADY installed must stay reachable on that same axis too.
  { href: "/wardrobe", label: "Wardrobe", iconName: "wardrobe" },
  // Editor (PLAN T206, SPEC F104.11) — the v2 editor mixes a base theme's palette with wardrobe
  // faces, transient client state only. Deliberately NOT gated by `requiresCatalog`, the SAME
  // reasoning as Wardrobe's own ungated entry immediately above: the base-theme and vendored role
  // picker options never touch the Community Catalog at all (GET /api/themes, GET /api/fonts/assignable
  // both read station-local/embedded data), so there is no catalog-reachability axis to gate on.
  { href: "/editor", label: "Editor", iconName: "editor" },
  { href: "/booth-log", label: "Booth log", iconName: "booth-log" },
  { href: "/health", label: "Health", iconName: "health" },
  { href: "/settings", label: "Settings", iconName: "settings" },
];

/**
 * The nav items to actually render (SPEC F90.1's fail-closed hide) — `catalogEnabled` defaults to
 * `false` so an isolated component render (no shell/layout above it, e.g. a jest test rendering
 * `<Sidebar />` bare) never shows an entry point to a feature it has no live signal for.
 */
export function visibleNavItems(catalogEnabled: boolean): NavItem[] {
  return NAV_ITEMS.filter((item) => item.requiresCatalog !== true || catalogEnabled);
}

/** True when `pathname` is the nav item's own route or a route nested under it. */
export function isActiveSection(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(`${href}/`);
}

/** 40px min touch target (SPEC F28.13) — nav links are `<a>` elements, so the
 * global `input/select/textarea/button` min-height rule in globals.css doesn't
 * reach them; this class list carries it explicitly instead. */
export const NAV_LINK_CLASSES =
  "flex min-h-10 items-center gap-2.5 rounded-[6px] px-3 py-2 text-[0.85rem] font-semibold transition-colors duration-[120ms] ease-out focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent";
