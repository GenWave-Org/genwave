import type { ReactNode } from "react";
import { LiveView } from "./LiveView";

// The full on-air view (SPEC F28.7–F28.8, STORY-088): now-playing hero and
// the complete play-history ring, client-polled via the shared usePoll hook
// (lib/use-poll.ts, shipped by the Dashboard in Q5). No SSR prefetch — auth
// is already enforced by middleware.ts on this route, and every card starts
// in its loading/skeleton state until its first poll resolves, same as
// /dashboard.
export default function LivePage(): ReactNode {
  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Live</h1>
      <div className="mt-4">
        <LiveView />
      </div>
    </main>
  );
}
