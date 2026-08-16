"use client";

import type { ReactNode } from "react";
import { Chip } from "@/components/ui/chip";
import { EmptyState } from "@/components/ui/empty-state";
import { parseIconPackDefinition } from "@/lib/icon-pack";
import { IconPackSpecimenRow } from "../_components/IconPackRenderer";
import { IconUninstallPackButton } from "./IconUninstallPackButton";
import { ProvenanceChip } from "./ProvenanceChip";
import type { IconPackSummaryDto } from "./types";

export interface IconWardrobeClientProps {
  /** Every installed pack, from `GET /api/icon-packs` (SPEC F130.4, PLAN T303/T304). */
  packs: IconPackSummaryDto[];
  /** Test-only injection point for the provenance chip's `formatDateStamp` call — the
   * WardrobeClient/AvatarWardrobeClient house idiom, not a bespoke one. */
  timeZone?: string;
  /** Same CTA swap every sibling tab performs (T203 review finding F3): a disabled catalog must
   * never leave this tab's empty state pointing at `/persona-catalog`, which itself 404s
   * off-catalog. Defaults to `false` — fail closed, matching every sibling tab's own posture. */
  catalogEnabled?: boolean;
  /**
   * The station's current `Station:IconPack` value (SPEC F130.4), off `GET /api/settings` —
   * server-resolved by `wardrobe/page.tsx`, the SAME settings-derived signal `layout.tsx`'s own
   * `ThemeSwitcher` read already establishes for a sibling setting. Drives the "Active" chip and
   * `IconUninstallPackButton`'s own fail-open confirm copy (STORY-337 AC6). Defaults to `""` — no
   * live signal, so no pack is ever falsely marked active.
   */
  activeSlug?: string;
}

/**
 * The Wardrobe's Icons tab (SPEC F130.4/F130.5, STORY-337, PLAN T304) — every installed icon pack,
 * each rendered as its own card: the pack's own slug (no display name exists on this schema — SPEC
 * F130.1), an "Active" chip when it names the station's current `Station:IconPack`, the "Installed
 * · ⟨slug⟩ · ⟨date⟩" provenance chip, its own declared icon count, and a specimen row drawing
 * every icon it declares SMALL, through the SAME safe renderer (`IconPackGlyph`, via
 * `IconPackSpecimenRow`) the active chrome itself uses. Read-only listing except uninstall (mirrors
 * `WardrobeClient`/`AvatarWardrobeClient`'s own shape): this component's own render issues no
 * requests; every card lists exactly what `GET /api/icon-packs` already returned.
 *
 * <b>DEFENSIVE PARSE, NOT TRUST (PLAN T304 rider 1).</b> `pack.definition` is this station's own
 * already-canonical, previously-validated text — and this component STILL runs it through
 * `parseIconPackDefinition`'s own from-scratch defensive parse before drawing anything, the same
 * discipline the pre-install shelf specimen (`IconDetailPanel`) applies to genuinely untrusted
 * remote text. A parse failure (should never happen for a row this station itself wrote) degrades
 * to a plain "unavailable" line, never a crash.
 */
export function IconWardrobeClient({ packs, timeZone, catalogEnabled = false, activeSlug = "" }: IconWardrobeClientProps): ReactNode {
  if (packs.length === 0) {
    return catalogEnabled ? (
      <EmptyState
        title="No icon packs installed"
        reason="Browse the Community Catalog to install an icon pack for this station."
        cta={{ label: "Browse the Community Catalog", href: "/persona-catalog" }}
      />
    ) : (
      <EmptyState
        title="No icon packs installed"
        reason="The Community Catalog is disabled — enable Community:CatalogIndexUrl in Settings to browse packs."
        cta={{ label: "Open Settings", href: "/settings" }}
      />
    );
  }

  return (
    <ul aria-label="Installed icon packs" className="flex flex-col gap-3">
      {packs.map((pack) => {
        const isActive = activeSlug !== "" && pack.slug === activeSlug;
        const definition = parseIconPackDefinition(pack.definition);
        return (
          <li key={pack.slug} className="rounded-[6px] border border-line bg-surface p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex flex-wrap items-center gap-2">
                <h2 className="font-display text-[1.1rem] text-ink">{pack.slug}</h2>
                {isActive && <Chip>Active</Chip>}
              </div>
              <div className="flex flex-wrap items-center gap-3">
                <ProvenanceChip importedFrom={pack.importedFrom} importedAt={pack.importedAt} timeZone={timeZone} />
                <IconUninstallPackButton slug={pack.slug} isActive={isActive} />
              </div>
            </div>

            <p className="mt-2 text-[0.75rem] text-mute">
              {pack.iconCount} icon{pack.iconCount === 1 ? "" : "s"}
            </p>

            <div className="mt-3">
              {definition === null ? (
                <p className="text-[0.85rem] text-mute">This pack&apos;s stored definition could not be read.</p>
              ) : (
                <IconPackSpecimenRow definition={definition} />
              )}
            </div>
          </li>
        );
      })}
    </ul>
  );
}
