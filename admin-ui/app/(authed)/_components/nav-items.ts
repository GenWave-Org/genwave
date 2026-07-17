import type { ReactNode } from "react";
import {
  CatalogIcon,
  DashboardIcon,
  LiveIcon,
  PersonaIcon,
  SafeContentIcon,
  SettingsIcon,
  type IconProps,
} from "./icons";

export interface NavItem {
  href: string;
  label: string;
  Icon: (props: IconProps) => ReactNode;
}

/**
 * Sidebar sections per SPEC F28.5, shared by the persistent desktop
 * `Sidebar` (≥1024px) and the `MobileNav` drawer (<1024px, SPEC F28.13) so
 * the two never drift. Libraries is deliberately absent — it lives under
 * the Catalog page's Libraries tab (Q7, SPEC F28.11); /libraries is now
 * only a redirect into that tab, never its own rendered route.
 */
export const NAV_ITEMS: NavItem[] = [
  { href: "/dashboard", label: "Dashboard", Icon: DashboardIcon },
  { href: "/live", label: "Live", Icon: LiveIcon },
  { href: "/catalog", label: "Catalog", Icon: CatalogIcon },
  { href: "/safe-content", label: "Safe content", Icon: SafeContentIcon },
  { href: "/personas", label: "Personas", Icon: PersonaIcon },
  { href: "/settings", label: "Settings", Icon: SettingsIcon },
];

/** True when `pathname` is the nav item's own route or a route nested under it. */
export function isActiveSection(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(`${href}/`);
}

/** 40px min touch target (SPEC F28.13) — nav links are `<a>` elements, so the
 * global `input/select/textarea/button` min-height rule in globals.css doesn't
 * reach them; this class list carries it explicitly instead. */
export const NAV_LINK_CLASSES =
  "flex min-h-10 items-center gap-2.5 rounded-[6px] px-3 py-2 text-[0.85rem] font-semibold transition-colors duration-[120ms] ease-out focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent";
