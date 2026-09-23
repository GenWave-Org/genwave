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

/** The stable group identities (SPEC F203.1, ARCHITECTURE's `NavGroup` shape) — the key a manual
 * collapse/expand toggle is remembered under (PLAN T559). Kept separate from `label` so a future
 * copy change can't silently invalidate every browser's stored toggle. */
export type NavGroupId = "station" | "media" | "tools" | "status";

/** A titled cluster of nav items (SPEC F203.1). */
export interface NavGroup {
  id: NavGroupId;
  label: string;
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
 * (≥1024px) and the `MobileNav` drawer (<1024px, SPEC F28.13) so the two never drift. `readonly`
 * (ARCHITECTURE) — this is a house constant, not a per-render working copy; `visibleNavGroups`
 * below always returns a fresh array rather than mutating one of these in place.
 */
export const NAV_GROUPS: readonly NavGroup[] = [
  {
    id: "station",
    label: "Station",
    items: [
      { href: "/safe-content", label: "Station sounds", iconName: "safe-content" },
      { href: "/ads", label: "Ads", iconName: "exploration" },
      { href: "/personas", label: "Personas", iconName: "persona" },
      { href: "/schedule", label: "Schedule", iconName: "schedule" },
      { href: "/shows", label: "Shows", iconName: "shows" },
    ],
  },
  {
    id: "media",
    label: "Media",
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
    id: "tools",
    label: "Tools",
    items: [
      { href: "/gardener", label: "Gardener", iconName: "restore" },
      { href: "/editor", label: "Theme Editor", iconName: "editor" },
    ],
  },
  {
    id: "status",
    label: "Status",
    items: [
      { href: "/booth-log", label: "Booth log", iconName: "booth-log" },
      { href: "/health", label: "Health", iconName: "health" },
    ],
  },
];

/** Rendered above every group. */
export const NAV_TOP: readonly NavItem[] = [{ href: "/dashboard", label: "Dashboard", iconName: "dashboard" }];

/** Rendered below every group. */
export const NAV_BOTTOM: readonly NavItem[] = [{ href: "/settings", label: "Settings", iconName: "settings" }];

/** The footer entries (About, Sign out) — rendered by `Sidebar`/`MobileNav` from this same data
 * (PLAN T559). About's own page (PLAN T561) lives at `app/(authed)/about/page.tsx`. */
export const NAV_FOOTER: readonly NavFooterEntry[] = [
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
export function visibleNavGroups(catalogEnabled: boolean, groups: readonly NavGroup[] = NAV_GROUPS): readonly NavGroup[] {
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

/** The id of the group that owns `pathname` (SPEC F203.2's route rule), or `undefined` when
 * `pathname` belongs to `NAV_TOP`/`NAV_BOTTOM` (e.g. `/dashboard`, `/settings`) rather than any
 * group. */
export function groupIdForPathname(pathname: string, groups: readonly NavGroup[] = NAV_GROUPS): NavGroupId | undefined {
  return groups.find((group) => group.items.some((item) => isActiveSection(pathname, item.href)))?.id;
}

/** 40px min touch target (SPEC F28.13) — nav links are `<a>` elements, so the
 * global `input/select/textarea/button` min-height rule in globals.css doesn't
 * reach them; this class list carries it explicitly instead. */
export const NAV_LINK_CLASSES =
  "flex min-h-10 items-center gap-2.5 rounded-[6px] px-3 py-2 text-[0.85rem] font-semibold transition-colors duration-[120ms] ease-out focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent";

/** A group's collapsible heading button (SPEC F203.2) — same 40px touch target as `NAV_LINK_CLASSES`
 * but styled as a micro-label (uppercase, tracked) rather than a destination, so a group heading
 * never reads as just another link. Used by `NavSections`, the shared render both `Sidebar` and
 * `MobileNav` mount. */
export const NAV_GROUP_HEADING_CLASSES =
  "flex min-h-10 w-full items-center justify-between rounded-[6px] px-3 py-2 text-[0.7rem] font-semibold uppercase tracking-[0.14em] text-mute transition-colors duration-[120ms] ease-out hover:bg-surface hover:text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent";
