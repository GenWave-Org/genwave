import type { HTMLAttributes, ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface ChipProps extends HTMLAttributes<HTMLSpanElement> {
  children?: ReactNode;
}

/**
 * The quiet bordered-pill chip (design-aesthetic skill) — 3px radius, `--line` border, `--mute`
 * text — used across the admin UI for a status word or a provenance stamp: source tags
 * (`SettingsForm`'s `SourceChip`), imported-theme provenance (`SettingsForm`'s
 * `ThemeProvenanceBadge`, `PersonaCatalogClient`'s theme detail panel), imported-persona provenance
 * (`PersonasClient`'s `ProvenanceBadge`), an installed font pack's provenance
 * (`WardrobeClient`'s `ProvenanceChip`), and a catalog font pack's bare "Installed" status
 * (`FontDetailPanel`). Every one of those five pre-existing sites carried its OWN copy of the same
 * className string (gh-#375 review carry-forward, N4) — this is the one extraction, children and
 * an optional `className` override are the only thing that ever varied. `className` merges via
 * `cn()` (the `Button`/`EmptyState` precedent) rather than replacing the base styling outright, so a
 * caller can ADD a layout concern (e.g. `PersonasClient`'s own `ml-2` — it sits inline right after a
 * name, unlike every other site's standalone placement) without repeating the visual treatment
 * itself. Every other native `<span>` attribute (`aria-label`, `data-source`, `data-testid`, …)
 * passes straight through via `...props`, the same `Button` idiom — `SourceChip`'s own
 * `aria-label`/`data-source` pair needs both.
 */
export function Chip({ children, className, ...props }: ChipProps): ReactNode {
  return (
    <span
      className={cn(
        "inline-flex w-fit items-center rounded-[3px] border border-line px-1.5 py-0.5 text-[0.68rem] text-mute",
        className
      )}
      {...props}
    >
      {children}
    </span>
  );
}
