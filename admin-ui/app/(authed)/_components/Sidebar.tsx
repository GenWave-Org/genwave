"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import { logout } from "@/app/login/actions";
import { cn } from "@/lib/utils";
import { Icon } from "./Icon";
import { NAV_BOTTOM, NAV_LINK_CLASSES, NAV_TOP, isActiveSection, visibleNavGroups } from "./nav-items";

interface SidebarProps {
  /**
   * Station name for the wordmark, server-fetched by the authed layout from
   * `GET /api/stations` (SPEC F44.7) with a "GenWave" fallback baked into
   * that fetch. Optional here — defaults to "GenWave" — so components tests
   * that render `<Sidebar />` in isolation, with no shell above them, still
   * see the product brand rather than an empty wordmark.
   */
  stationName?: string;
  /**
   * Whether the Persona Catalog entry point should be listed (PLAN T102, SPEC F90.1) —
   * server-resolved by the authed layout from the live `Community:CatalogIndexUrl` setting.
   * Defaults to `false` (fail-closed) so an isolated render never shows a link into a feature it
   * has no live signal for.
   */
  catalogEnabled?: boolean;
}

/**
 * Persistent shell sidebar (SPEC F28.5) — visible at ≥1024px only. Below
 * that breakpoint it is replaced by `MobileNav`'s drawer (SPEC F28.13),
 * which renders the same nav model behind a focus-trapped Radix dialog;
 * this component still mounts (so `usePathname` stays live for the active-
 * section highlight) but is hidden via `lg:flex` rather than unmounted.
 *
 * Renders `NAV_TOP`, then the visible groups' items flattened in order, then `NAV_BOTTOM` — a flat
 * list, same as before the grouped model (SPEC F203.1) landed. Group headings, collapsing and the
 * `NAV_FOOTER` entries are PLAN T559's job.
 */
export function Sidebar({ stationName = "GenWave", catalogEnabled = false }: SidebarProps): ReactNode {
  const pathname = usePathname();
  const items = [...NAV_TOP, ...visibleNavGroups(catalogEnabled).flatMap((group) => group.items), ...NAV_BOTTOM];

  return (
    <aside className="hidden w-[215px] shrink-0 flex-col border-r-2 border-line bg-surface-2 lg:flex">
      <div className="px-5 py-5">
        <span className="font-display text-xl italic text-ink">{stationName}</span>
      </div>

      <nav aria-label="Sections" className="flex-1 px-3">
        <ul className="flex flex-col gap-1">
          {items.map(({ href, label, iconName }) => {
            const active = isActiveSection(pathname, href);
            return (
              <li key={href}>
                <Link
                  href={href}
                  aria-current={active ? "page" : undefined}
                  className={cn(
                    NAV_LINK_CLASSES,
                    active
                      ? "bg-accent/10 text-accent"
                      : "text-mute hover:bg-surface hover:text-ink"
                  )}
                >
                  <Icon name={iconName} className="shrink-0" />
                  {label}
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>

      <form action={logout} className="border-t border-line px-3 py-4">
        <button type="submit" className={cn(NAV_LINK_CLASSES, "w-full text-mute hover:bg-surface hover:text-ink")}>
          <Icon name="sign-out" className="shrink-0" />
          Sign out
        </button>
      </form>
    </aside>
  );
}
