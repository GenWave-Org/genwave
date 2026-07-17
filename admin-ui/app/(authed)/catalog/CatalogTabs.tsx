import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export type CatalogTab = "tracks" | "libraries";

interface CatalogTabsProps {
  activeTab: CatalogTab;
}

interface TabDef {
  id: CatalogTab;
  label: string;
  href: string;
}

const TABS: TabDef[] = [
  { id: "tracks", label: "Tracks", href: "/catalog" },
  { id: "libraries", label: "Libraries", href: "/catalog?tab=libraries" },
];

/**
 * Tracks | Libraries tab strip for the Catalog page (SPEC F28.11, STORY-089
 * AC4) — URL-driven via `?tab=`, no client state. Libraries folds under
 * Catalog here instead of its own sidebar item (removed at Q3).
 */
export function CatalogTabs({ activeTab }: CatalogTabsProps): ReactNode {
  return (
    <nav aria-label="Catalog sections" className="flex gap-1 border-b-2 border-line">
      {TABS.map((tab) => {
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
