import Link from "next/link";
import type { ReactNode } from "react";
import { cn } from "@/lib/utils";
import type { GardenerKind } from "@/lib/gardener-api";
import { buildGardenerHref, GARDENER_PAGE_SIZES, type GardenerPageSize } from "./gardener-paging";

interface GardenerPageSizePickerProps {
  kind: GardenerKind;
  limit: GardenerPageSize;
}

/**
 * Rows-per-page picker (SPEC F153.10 rider 2026-08-31; STORY-382 AC3-AC4): plain anchors for each
 * of {@link GARDENER_PAGE_SIZES}, the same "no client JS" pager idiom the catalog's own Previous/
 * Next links use — text links, never icon-only (T378 law). Picking a size always resets to page 1
 * (`buildGardenerHref` never carries a `page` param). Chip-scale radius and a 40px touch target
 * (T387 review LOW-4 — design-aesthetic's chip/badge sizing, matching `TabStrip`'s own `min-h-10`).
 */
export function GardenerPageSizePicker({ kind, limit }: GardenerPageSizePickerProps): ReactNode {
  return (
    <div className="mt-3 flex items-center gap-2 text-[0.8rem] text-mute">
      <span id="gardener-page-size-label">Rows per page</span>
      <div role="group" aria-labelledby="gardener-page-size-label" className="flex items-center gap-1">
        {GARDENER_PAGE_SIZES.map((size) => {
          const active = size === limit;
          return (
            <Link
              key={size}
              href={buildGardenerHref(kind, size)}
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
