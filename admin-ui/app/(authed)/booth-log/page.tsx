import type { ReactNode } from "react";
import { BoothLogView } from "./BoothLogView";

// The booth log's narrative feed (PLAN T40, STORY-195, SPEC F72.1-F72.2): newest-first,
// keyset-paged, client-polled via useBoothLogFeed (lib/use-poll.ts underneath, the same shared
// hook the Dashboard/Live pages use). No SSR prefetch — auth is already enforced by
// middleware.ts on this route, and the feed starts in its loading/skeleton state until the first
// poll resolves, same as /dashboard and /live.
export default function BoothLogPage(): ReactNode {
  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Booth log</h1>
      <div className="mt-4">
        <BoothLogView />
      </div>
    </main>
  );
}
