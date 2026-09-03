import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface PageSizePickerProps<TSize extends number> {
  sizes: readonly TSize[];
  limit: TSize;
  /** The href for a given size — every OTHER active query param (tab/kind, filters) is the
   * caller's own concern, not this component's; mirrors `Pager`'s own `hrefFor` shape exactly (one
   * page-scoped value in, one URL out) — the "ready shape" PLAN T404 review fold (b) points at.
   * Generic over `TSize` (not a bare `number`, unlike `Pager`'s own unconstrained `page`) so a
   * caller's own strict page-size literal union (`AdsPageSize`/`GardenerPageSize`) flows straight
   * into its `buildXHref(tab, size)` closure with no cast at the call site. */
  hrefFor: (size: TSize) => string;
}

/**
 * Rows-per-page picker (SPEC F153.10 rider 2026-08-31; STORY-382 AC3-AC4; PLAN T404 review fold
 * b) — plain anchors for each of `sizes`, the same "no client JS" pager idiom `Pager`'s own
 * Previous/Next links use — text links, never icon-only (T378 law). Picking a size always resets
 * to page 1: that's `hrefFor`'s own contract, enforced by every caller's `buildXHref(tab, size)`
 * (never carrying a `page` param), not by this component.
 *
 * The ONE shared implementation (mirrors `Pager`'s own extraction one component over) — the
 * Gardener page (`gardener/GardenerPageSizePicker.tsx`) and the Ads page had each grown a
 * byte-identical copy of this exact markup, differing only in which `kind`/`tab`-scoped href
 * builder they closed over. That difference is now the caller's own `hrefFor` closure; this
 * component only ever sees `size → string`.
 */
export function PageSizePicker<TSize extends number>({ sizes, limit, hrefFor }: PageSizePickerProps<TSize>): ReactNode {
  return (
    <div className="mt-3 flex items-center gap-2 text-[0.8rem] text-mute">
      <span id="page-size-picker-label">Rows per page</span>
      <div role="group" aria-labelledby="page-size-picker-label" className="flex items-center gap-1">
        {sizes.map((size) => {
          const active = size === limit;
          return (
            <Link
              key={size}
              href={hrefFor(size)}
              aria-current={active ? "page" : undefined}
              className={cn(
                "flex min-h-10 items-center rounded-[3px] px-2",
                active ? "font-semibold text-accent" : "hover:text-ink"
              )}
            >
              {size}
            </Link>
          );
        })}
      </div>
    </div>
  );
}
