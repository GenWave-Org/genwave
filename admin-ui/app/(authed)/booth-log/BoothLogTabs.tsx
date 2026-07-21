import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export type BoothLogTab = "log" | "llm-calls";

interface BoothLogTabsProps {
  activeTab: BoothLogTab;
}

interface TabDef {
  id: BoothLogTab;
  label: string;
  href: string;
}

const TABS: TabDef[] = [
  { id: "log", label: "Booth log", href: "/booth-log" },
  { id: "llm-calls", label: "LLM calls", href: "/booth-log?tab=llm-calls" },
];

/**
 * Booth log | LLM calls tab strip (PLAN T41, STORY-196) — URL-driven via `?tab=`, no client state,
 * same shape as the Catalog page's own Tracks | Libraries tabs (CatalogTabs). The LLM call
 * inspector folds under this page rather than earning its own sidebar item: the nav was already
 * getting full after T40 added Booth log, and both surfaces are the same "operational narrative"
 * epic (SPEC F72/F73) — a debug tab on an existing operator page, not a new top-level destination.
 */
export function BoothLogTabs({ activeTab }: BoothLogTabsProps): ReactNode {
  return (
    <nav aria-label="Booth log sections" className="flex gap-1 border-b-2 border-line">
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
