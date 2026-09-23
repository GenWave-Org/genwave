"use client";

import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import { NavSections } from "./NavSections";

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
 * The wordmark header is the only markup this component owns — the rest (`NAV_TOP`, the
 * collapsible groups, `NAV_BOTTOM`, the footer) is `NavSections`, shared verbatim with
 * `MobileNav` (SPEC F203.1–F203.2).
 */
export function Sidebar({ stationName = "GenWave", catalogEnabled = false }: SidebarProps): ReactNode {
  const pathname = usePathname();

  return (
    <aside className="hidden w-[215px] shrink-0 flex-col border-r-2 border-line bg-surface-2 lg:flex">
      <div className="px-5 py-5">
        <span className="font-display text-xl italic text-ink">{stationName}</span>
      </div>

      <NavSections pathname={pathname} catalogEnabled={catalogEnabled} />
    </aside>
  );
}
