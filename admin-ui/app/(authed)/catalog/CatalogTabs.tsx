import type { ReactNode } from "react";
import { TabStrip, type TabStripTab } from "@/components/ui/tab-strip";

export type CatalogTab = "tracks" | "libraries";

interface CatalogTabsProps {
  activeTab: CatalogTab;
}

const TABS: TabStripTab<CatalogTab>[] = [
  { id: "tracks", label: "Tracks", href: "/catalog" },
  { id: "libraries", label: "Libraries", href: "/catalog?tab=libraries" },
];

/**
 * Tracks | Libraries tab strip for the Catalog page (SPEC F28.11, STORY-089
 * AC4) — URL-driven via `?tab=`, no client state. Libraries folds under
 * Catalog here instead of its own sidebar item (removed at Q3). Markup lives
 * in the shared `TabStrip` (gh-#393 extraction) — this wrapper owns only the
 * tab defs.
 */
export function CatalogTabs({ activeTab }: CatalogTabsProps): ReactNode {
  return <TabStrip tabs={TABS} activeTab={activeTab} ariaLabel="Catalog sections" />;
}
