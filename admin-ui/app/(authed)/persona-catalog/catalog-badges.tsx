import type { ReactNode } from "react";

/**
 * Small, kind-agnostic shelf/detail badges shared across the catalog surface (SPEC F90.4a) — split
 * out of `PersonaCatalogClient.tsx` at PLAN T255 so `ShowCardReviewModal` (a sibling component, not
 * a descendant) can render the SAME badges without either file importing the other: before this
 * split, `ShowCardReviewModal` importing these two from `PersonaCatalogClient.tsx` while that file
 * imports `ShowCardReviewModal` back would have been a circular module dependency — harmless for
 * hoisted function declarations that are only ever CALLED during render, well after both modules
 * finish loading, but still the kind of coupling worth designing away rather than relying on. This
 * mirrors `Chip`'s own extraction precedent (`components/ui/chip.tsx`'s own remarks): a component
 * with more than one real consumer earns a shared home instead of a second, drifting copy.
 */

/** The 18+ badge (SPEC F90.4a) — ALWAYS shown on a mature entry, never behind a toggle (ruled).
 * Pill treatment (999px radius) per the Wireless state-badge convention, brass (`--accent-2`) so
 * it reads as a clear, calm label rather than an alarm. */
export function MatureBadge(): ReactNode {
  return (
    <span
      aria-label="Mature content"
      className="inline-flex w-fit shrink-0 items-center rounded-[999px] border border-accent-2 px-2 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-accent-2"
    >
      18+
    </span>
  );
}

/** `bestFor[]` genre chips (SPEC F90.4a) — 3px-radius bordered source-tag treatment, rendered only
 * when present (an entry with none renders nothing, not an empty container). Shared across every
 * kind's shelf card/detail view so none of them drift on how a chip looks. */
export function BestForChips({ items }: { items: string[] }): ReactNode {
  if (items.length === 0) return null;

  return (
    <ul aria-label="Best for" className="m-0 flex list-none flex-wrap gap-1.5 p-0">
      {items.map((tag) => (
        <li key={tag}>
          <span className="inline-flex items-center rounded-[3px] border border-line bg-surface-2 px-1.5 py-0.5 text-[0.72rem] text-mute">
            {tag}
          </span>
        </li>
      ))}
    </ul>
  );
}
