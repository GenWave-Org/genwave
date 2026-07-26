import type { ReactNode } from "react";
import { Skeleton } from "@/components/ui/skeleton";

const SKELETON_CARD_COUNT = 6;

/**
 * Route-level Suspense fallback for /persona-catalog (SPEC F28.10). Next.js shows this
 * automatically while the segment's server render (the index fetch in page.tsx) is in flight —
 * mirrors catalog/loading.tsx's own shape, sized to a card grid instead of a table.
 */
export default function PersonaCatalogLoading(): ReactNode {
  return (
    <main>
      <Skeleton className="h-6 w-48" />
      <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: SKELETON_CARD_COUNT }, (_, i) => (
          <Skeleton key={i} className="h-28 w-full" />
        ))}
      </div>
    </main>
  );
}
