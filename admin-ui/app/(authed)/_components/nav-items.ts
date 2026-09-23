import type { IconName } from "./Icon";

/** One clickable nav destination (SPEC F130.2 icon-name contract, PLAN T304 — `iconName` resolves
 * through `Icon` at render time, never a direct `icons.tsx` import, so an installed icon pack swaps
 * every nav glyph without this file changing). */
export interface NavItem {
  href: string;
  label: string;
  iconName: IconName;
  /** True for a nav item that only exists when its own feature is enabled (PLAN T102, SPEC
   * F90.1) — hidden until `catalogEnabled`. No other item needs a gate today. */
  requiresCatalog?: boolean;
}

/** A titled cluster of nav items (SPEC F203.1). */
export interface NavGroup {
  title: string;
  items: NavItem[];
}

/**
 * A footer entry is either a real link or the sign-out action — never both, modeled as a
 * discriminated union so the "sign-out isn't a link" fact can't be lost to a fake href. `Sidebar`/
 * `MobileNav` render the sign-out action as the `logout` server-action form they already own; this
 * entry only carries what the footer displays.
 */
export type NavFooterEntry =
  | { kind: "link"; href: string; label: string; iconName?: IconName }
  | { kind: "sign-out"; label: string; iconName?: IconName };

/**
 * Grouped sidebar/drawer model (SPEC F203.1, STORY-471) shared by the persistent desktop `Sidebar`
 * (≥1024px) and the `MobileNav` drawer (<1024px, SPEC F28.13) so the two never drift.
 */
export const NAV_GROUPS: NavGroup[] = [
  {
    title: "Station",
    items: [
      { href: "/safe-content", label: "Station sounds", iconName: "safe-content" },
      { href: "/ads", label: "Ads", iconName: "exploration" },
      { href: "/personas", label: "Personas", iconName: "persona" },
      { href: "/schedule", label: "Schedule", iconName: "schedule" },
      { href: "/shows", label: "Shows", iconName: "shows" },
    ],
  },
  {
    title: "Media",
    items: [
      { href: "/catalog", label: "Catalog", iconName: "catalog" },
      { href: "/announcements", label: "Announcements", iconName: "announcements" },
      {
        href: "/persona-catalog",
        label: "Community Catalog",
        iconName: "persona-catalog",
        requiresCatalog: true,
      },
    ],
  },
  {
    title: "Tools",
    items: [
      { href: "/gardener", label: "Gardener", iconName: "restore" },
      { href: "/editor", label: "Theme Editor", iconName: "editor" },
    ],
  },
  {
    title: "Status",
    items: [
      { href: "/booth-log", label: "Booth log", iconName: "booth-log" },
      { href: "/health", label: "Health", iconName: "health" },
    ],
  },
];

/** Rendered above every group. */
export const NAV_TOP: NavItem[] = [{ href: "/dashboard", label: "Dashboard", iconName: "dashboard" }];

/** Rendered below every group. */
export const NAV_BOTTOM: NavItem[] = [{ href: "/settings", label: "Settings", iconName: "settings" }];

/** The footer entries (About, Sign out). Sidebar/MobileNav render them once PLAN T559 wires the footer; About's page arrives at PLAN T561. */
export const NAV_FOOTER: NavFooterEntry[] = [
  { kind: "link", href: "/about", label: "About" },
  { kind: "sign-out", label: "Sign out", iconName: "sign-out" },
];

/**
 * The groups to actually render (SPEC F90.1's fail-closed hide) — `Sidebar`/`MobileNav` default
 * their `catalogEnabled` prop to `false` so an isolated component render (no shell/layout above it, e.g. a jest test rendering
 * `<Sidebar />` bare) never shows an entry point to a feature it has no live signal for. A group
 * left with zero visible items after the gate is dropped entirely (SPEC F203.3). `groups` defaults
 * to `NAV_GROUPS` but takes an override so a spec can exercise an all-gated group without one
 * existing in the house model.
 */
export function visibleNavGroups(catalogEnabled: boolean, groups: NavGroup[] = NAV_GROUPS): NavGroup[] {
  return groups
    .map((group) => ({
      ...group,
      items: group.items.filter((item) => item.requiresCatalog !== true || catalogEnabled),
    }))
    .filter((group) => group.items.length > 0);
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
