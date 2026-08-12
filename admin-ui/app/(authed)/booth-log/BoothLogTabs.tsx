import type { ReactNode } from "react";
import { TabStrip, type TabStripTab } from "@/components/ui/tab-strip";

export type BoothLogTab = "log" | "llm-calls";

interface BoothLogTabsProps {
  activeTab: BoothLogTab;
}

const TABS: TabStripTab<BoothLogTab>[] = [
  { id: "log", label: "Booth log", href: "/booth-log" },
  { id: "llm-calls", label: "LLM calls", href: "/booth-log?tab=llm-calls" },
];

/**
 * Booth log | LLM calls tab strip (PLAN T41, STORY-196) — URL-driven via `?tab=`, no client state.
 * The LLM call inspector folds under this page rather than earning its own sidebar item: the nav
 * was already getting full after T40 added Booth log, and both surfaces are the same "operational
 * narrative" epic (SPEC F72/F73) — a debug tab on an existing operator page, not a new top-level
 * destination. Markup lives in the shared `TabStrip` (gh-#393 extraction) — this wrapper owns only
 * the tab defs.
 */
export function BoothLogTabs({ activeTab }: BoothLogTabsProps): ReactNode {
  return <TabStrip tabs={TABS} activeTab={activeTab} ariaLabel="Booth log sections" />;
}
