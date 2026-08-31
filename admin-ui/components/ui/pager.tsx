import Link from "next/link";
import type { ReactNode } from "react";

export interface PagerProps {
  page: number;
  pages: number;
  /** The href for a given target page — every OTHER active query param (filters, tab, limit) is
   * the caller's own concern, not this component's; this is just "page N" plugged into whatever
   * URL shape the caller already owns. */
  hrefFor: (page: number) => string;
}

/**
 * "Page N of M" + Previous/Next plain anchors (T387 review MED-3) — the ONE pager implementation
 * shared by the Catalog (`catalog/page.tsx`) and Gardener (`gardener/page.tsx`) pages, which had
 * grown byte-identical copies of this same markup independently. No client JS: `page`/`pages`
 * alone decide which anchors render — a `page` past `pages` (a legal, beyond-the-end request, SPEC
 * F153.10 rider) still renders a live Previous, just no Next.
 */
export function Pager({ page, pages, hrefFor }: PagerProps): ReactNode {
  if (pages <= 1) return null;

  return (
    <nav aria-label="Pagination" className="mt-4 flex items-center gap-3 text-[0.82rem] text-mute">
      {page > 1 && (
        <Link href={hrefFor(page - 1)} className="text-accent hover:underline">
          Previous
        </Link>
      )}
      <span>
        Page {page} of {pages}
      </span>
      {page < pages && (
        <Link href={hrefFor(page + 1)} className="text-accent hover:underline">
          Next
        </Link>
      )}
    </nav>
  );
}
