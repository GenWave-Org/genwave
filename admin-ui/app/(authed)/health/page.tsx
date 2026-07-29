import type { ReactNode } from "react";
import { HealthView } from "./HealthView";

// The Health page (gh-#148): a container-level view of the running stack — pretty `docker stats`,
// one card per service, fed by GET /api/health/containers (the api asks its allowlisted
// socket-proxy sidecar; the browser never holds a path to the Docker socket). No SSR prefetch —
// auth is already enforced by middleware.ts on this route, and the view starts in its
// loading/skeleton state until the first poll resolves, same as /dashboard and /booth-log.
export default function HealthPage(): ReactNode {
  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Health</h1>
      <div className="mt-6">
        <HealthView />
      </div>
    </main>
  );
}
