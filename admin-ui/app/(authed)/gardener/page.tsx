import type { ReactNode } from "react";
import { GardenerView } from "./GardenerView";

// The Library Gardener's own admin page (SPEC F153.10, STORY-374 AC9, STORY-376 AC6, PLAN T378,
// gh-#529): one section per rot-finding kind, each row offering the existing eligibility/never-play/
// re-enrich/dismiss verbs (plus a section-level Purge unavailable for dead files), and "Keep this
// one" on a near-duplicate group. No SSR prefetch, same posture as /live and /booth-log — auth is
// already enforced by middleware.ts on this route, and GardenerView loads its own data client-side
// once on mount (no polling: this is a curation console an operator opens to act on, not a live
// dashboard).
export default function GardenerPage(): ReactNode {
  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Gardener</h1>
      <div className="mt-4">
        <GardenerView />
      </div>
    </main>
  );
}
