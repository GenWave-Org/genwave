import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

/** One tab in a {@link TabStrip} — `id` doubles as the `?tab=` value its page resolves. */
export interface TabStripTab<TId extends string = string> {
  id: TId;
  label: string;
  href: string;
}

interface TabStripProps<TId extends string> {
  tabs: readonly TabStripTab<TId>[];
  activeTab: TId;
  /** Names the `<nav>` landmark (e.g. "Catalog sections") — every strip labels its own region. */
  ariaLabel: string;
}

/**
 * URL-driven tab strip (gh-#393 extraction, the gh-#375 `Chip` precedent applied to tabs):
 * `CatalogTabs` and `BoothLogTabs` were byte-identical modulo their tab defs — this is the one
 * shared implementation, and the Wardrobe/shelf kind tabs (gh-#393/gh-#372) build on it rather
 * than minting a fourth copy. Plain `<Link>`s + `aria-current`, no client state: the active tab is
 * whatever the URL says, so a strip works in a server component and survives refresh/share.
 */
export function TabStrip<TId extends string>({ tabs, activeTab, ariaLabel }: TabStripProps<TId>): ReactNode {
  return (
    <nav aria-label={ariaLabel} className="flex gap-1 border-b-2 border-line">
      {tabs.map((tab) => {
        const active = tab.id === activeTab;
        return (
          <Link
            key={tab.id}
            href={tab.href}
            aria-current={active ? "page" : undefined}
            className={cn(
              "-mb-[2px] flex min-h-10 items-center border-b-2 px-3 py-2 text-[0.82rem] font-semibold transition-colors duration-[120ms] ease-out",
              active ? "border-accent text-accent" : "border-transparent text-mute hover:text-ink"
            )}
          >
            {tab.label}
          </Link>
        );
      })}
    </nav>
  );
}
