import type { ReactNode } from "react";
import { DashboardView } from "./DashboardView";

// Post-login landing (SPEC F28.7–F28.8): now-playing card, station status
// tiles, recent plays — all client-polled via the shared usePoll hook
// (lib/use-poll.ts, also consumed by Live in Q6).
export default function DashboardPage(): ReactNode {
  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Dashboard</h1>
      <div className="mt-4">
        <DashboardView />
      </div>
    </main>
  );
}
