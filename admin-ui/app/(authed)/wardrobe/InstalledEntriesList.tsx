"use client";

import type { ReactNode } from "react";
import { Chip } from "@/components/ui/chip";
import { EmptyState } from "@/components/ui/empty-state";
import { formatDateStamp } from "@/lib/format-clock";
import type { InstalledEntryRow } from "./types";

export interface InstalledEntriesListProps {
  /** Genuinely-imported rows of one kind — see `InstalledEntryRow`'s own remarks. */
  rows: InstalledEntryRow[];
  /** Names the `<ul>` (e.g. "Hired personas") — mirrors `WardrobeClient`'s own "Installed font packs". */
  ariaLabel: string;
  /** The kind's own provenance verb — "Hired" for personas (the F90.7/T105 wording), "Imported" for
   * themes/shows (the Settings/shelf wording); fonts keep their own "Installed" chip in
   * `WardrobeClient`. The chip is otherwise the same db/25 "⟨verb⟩ · ⟨slug⟩ · ⟨date⟩" shape. */
  provenanceVerb: string;
  /** Empty-state copy (SPEC F28.10 — name the reason, calm radio-operator tone). */
  emptyTitle: string;
  emptyReason: string;
  /** Same CTA swap `WardrobeClient` performs (T203 review finding F3): a disabled catalog must never
   * leave an empty tab pointing at `/persona-catalog`, which itself 404s off-catalog. */
  catalogEnabled?: boolean;
  /** Test-only `formatDateStamp` zone pin — the PersonasClient/SettingsForm idiom (T105/T187). */
  timeZone?: string;
}

/**
 * One non-font wardrobe tab's listing (gh-#393): read-only cards — name, an optional secondary
 * line, and the provenance chip. Deliberately action-free: retiring a persona / removing a theme /
 * deleting a show each already live on that kind's own page with their own confirm flows — the
 * Wardrobe silos WHAT is installed per kind (the gh-#393 ask), it does not become a second place to
 * operate on them (the one exception, font uninstall, predates this page's widening and stays on
 * the Fonts tab). `name`/`detail` render as plain text nodes ONLY — see `InstalledEntryRow`.
 */
export function InstalledEntriesList({
  rows,
  ariaLabel,
  provenanceVerb,
  emptyTitle,
  emptyReason,
  catalogEnabled = false,
  timeZone,
}: InstalledEntriesListProps): ReactNode {
  if (rows.length === 0) {
    return catalogEnabled ? (
      <EmptyState
        title={emptyTitle}
        reason={emptyReason}
        cta={{ label: "Browse the Community Catalog", href: "/persona-catalog" }}
      />
    ) : (
      <EmptyState
        title={emptyTitle}
        reason="The Community Catalog is disabled — enable Community:CatalogIndexUrl in Settings to browse the shelf."
        cta={{ label: "Open Settings", href: "/settings" }}
      />
    );
  }

  return (
    <ul aria-label={ariaLabel} className="flex flex-col gap-3">
      {rows.map((row) => (
        <li key={row.slug} className="rounded-[6px] border border-line bg-surface p-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 className="font-display text-[1.1rem] text-ink">{row.name}</h2>
            <Chip>{`${provenanceVerb} · ${row.importedFrom} · ${formatDateStamp(row.importedAt, { timeZone })}`}</Chip>
          </div>
          {row.detail !== null && row.detail !== "" && (
            <p className="mt-2 text-[0.85rem] text-mute">{row.detail}</p>
          )}
        </li>
      ))}
    </ul>
  );
}
