import type { ReactNode } from "react";
import { Skeleton } from "@/components/ui/skeleton";

const SKELETON_ROW_COUNT = 6;

/**
 * Route-level Suspense fallback for /catalog (SPEC F28.10, STORY-089 AC5).
 * Next.js shows this automatically while the segment's server render is in
 * flight — on the initial navigation, on every filter/pagination
 * navigation (searchParams change), and on `router.refresh()` after a
 * successful bulk action (CatalogToolbar) or library mutation
 * (LibrariesTab) re-renders the page's server data.
 */
export default function CatalogLoading(): ReactNode {
  return (
    <main>
      <Skeleton className="h-6 w-28" />
      <div className="mt-4">
        <Skeleton className="h-8 w-72" />
      </div>
      <div className="mt-6 space-y-2">
        {Array.from({ length: SKELETON_ROW_COUNT }, (_, i) => (
          <Skeleton key={i} className="h-10 w-full" />
        ))}
      </div>
    </main>
  );
}
