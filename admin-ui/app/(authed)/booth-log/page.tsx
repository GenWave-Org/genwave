import type { ReactNode } from "react";
import { BoothLogTabs, type BoothLogTab } from "./BoothLogTabs";
import { BoothLogView } from "./BoothLogView";
import { LlmCallsView } from "./LlmCallsView";

interface BoothLogPageProps {
  searchParams: Promise<{ tab?: string }>;
}

function resolveTab(tab: string | undefined): BoothLogTab {
  return tab === "llm-calls" ? "llm-calls" : "log";
}

// The booth log's narrative feed (PLAN T40, STORY-195, SPEC F72.1-F72.2): newest-first,
// keyset-paged, client-polled via useBoothLogFeed (lib/use-poll.ts underneath, the same shared
// hook the Dashboard/Live pages use). No SSR prefetch — auth is already enforced by
// middleware.ts on this route, and the feed starts in its loading/skeleton state until the first
// poll resolves, same as /dashboard and /live.
//
// The LLM call inspector (PLAN T41, STORY-196, SPEC F73.1-F73.2) folds in here as a second,
// URL-driven tab (`?tab=llm-calls`) rather than its own sidebar item — see BoothLogTabs' own
// remarks for why.
export default async function BoothLogPage({ searchParams }: BoothLogPageProps): Promise<ReactNode> {
  const sp = await searchParams;
  const activeTab = resolveTab(sp.tab);

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Booth log</h1>
      <div className="mt-4">
        <BoothLogTabs activeTab={activeTab} />
      </div>
      <div className="mt-6">
        {activeTab === "llm-calls" ? <LlmCallsView /> : <BoothLogView />}
      </div>
    </main>
  );
}
