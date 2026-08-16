"use client";

import type { ReactNode } from "react";
import { Chip } from "@/components/ui/chip";
import { EmptyState } from "@/components/ui/empty-state";
import { formatDateStamp } from "@/lib/format-clock";
import { clampPackDisplayText } from "../persona-catalog/avatar-format";
import { prettifySlug } from "../persona-catalog/format-slug";
import { AvatarUninstallPackButton } from "./AvatarUninstallPackButton";
import type { AvatarPackSummaryDto } from "./types";

export interface AvatarWardrobeClientProps {
  /** Every installed pack, from `GET /api/avatar-packs` (SPEC F128.3, PLAN T294). */
  packs: AvatarPackSummaryDto[];
  /** Test-only injection point for the provenance chip's `formatDateStamp` call — the
   * WardrobeClient/PersonasClient/SettingsForm house idiom (T105/T187), not a bespoke one. */
  timeZone?: string;
  /** Same CTA swap `WardrobeClient`/`InstalledEntriesList` perform (T203 review finding F3): a
   * disabled catalog must never leave this tab's empty state pointing at `/persona-catalog`, which
   * itself 404s off-catalog. Defaults to `false` — fail closed, matching every sibling tab's own
   * posture. */
  catalogEnabled?: boolean;
}

/** Provenance chip — "Installed · &lt;slug&gt; · &lt;date&gt;" (the db/25 pattern) — mirrors
 * `WardrobeClient`'s own `ProvenanceChip` exactly, one directory over (that component stays
 * file-local to `WardrobeClient.tsx`, so this is its own small copy rather than reaching into
 * another file's non-exported function). */
function ProvenanceChip({ importedFrom, importedAt, timeZone }: { importedFrom: string; importedAt: string; timeZone?: string }): ReactNode {
  return <Chip>{`Installed · ${importedFrom} · ${formatDateStamp(importedAt, { timeZone })}`}</Chip>;
}

/**
 * The Wardrobe's Avatars tab (SPEC F128.3, F128.5, STORY-332, PLAN T294) — every installed avatar
 * pack, each rendered as its own card: display name (the manifest's own `packName`, clamped — PLAN
 * T294 rider 2 — falling back to `prettifySlug(slug)` on the should-never-happen re-parse failure,
 * the SAME slug-derived fallback every other kind's card already uses when it has no separate
 * display name to show), the "Installed · ⟨slug⟩ · ⟨date⟩" provenance chip (db/25 pattern), and its
 * OWN item grid — every item's clamped name plus a "Suggested: ⟨persona⟩" chip where the pack's
 * manifest named one (an OFFER, never applied by anything this listing renders — SPEC F128.5).
 * Mirrors `WardrobeClient`'s own read-only-except-uninstall shape: this component's own render
 * issues no requests of its own; every card lists exactly what `GET /api/avatar-packs` already
 * returned. The one action a card offers, uninstalling, is `AvatarUninstallPackButton`'s own
 * self-contained concern.
 *
 * <b>PLAIN TEXT ONLY, EVERYWHERE (PLAN T294 rider 2).</b> `pack.name` and each item's own `name` are
 * unbounded free-form prose straight off a remote manifest with NO server-side length gate on this
 * read (see `avatar-format.ts`'s own remarks) — both render as bare React text-node children ONLY,
 * through the shared `clampPackDisplayText` clamp, React's default escaping, never
 * `dangerouslySetInnerHTML`.
 */
export function AvatarWardrobeClient({ packs, timeZone, catalogEnabled = false }: AvatarWardrobeClientProps): ReactNode {
  if (packs.length === 0) {
    return catalogEnabled ? (
      <EmptyState
        title="No avatar packs installed"
        reason="Browse the Community Catalog to install an avatar pack for this station."
        cta={{ label: "Browse the Community Catalog", href: "/persona-catalog" }}
      />
    ) : (
      <EmptyState
        title="No avatar packs installed"
        reason="The Community Catalog is disabled — enable Community:CatalogIndexUrl in Settings to browse packs."
        cta={{ label: "Open Settings", href: "/settings" }}
      />
    );
  }

  return (
    <ul aria-label="Installed avatar packs" className="flex flex-col gap-3">
      {packs.map((pack) => {
        const displayName = clampPackDisplayText(pack.name ?? prettifySlug(pack.slug));
        return (
          <li key={pack.slug} className="rounded-[6px] border border-line bg-surface p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h2 className="font-display text-[1.1rem] text-ink">{displayName}</h2>
              <div className="flex flex-wrap items-center gap-3">
                <ProvenanceChip importedFrom={pack.importedFrom} importedAt={pack.importedAt} timeZone={timeZone} />
                <AvatarUninstallPackButton slug={pack.slug} displayName={displayName} />
              </div>
            </div>

            {pack.items.length === 0 ? (
              <p className="mt-3 text-[0.85rem] text-mute">This pack declares no items.</p>
            ) : (
              <ul aria-label={`${displayName} items`} className="mt-3 flex flex-wrap gap-2">
                {pack.items.map((item, index) => (
                  // Item names have no uniqueness guarantee at THIS altitude for a React key (the
                  // store's own UNIQUE(pack_id, name) constraint is a DIFFERENT invariant than "safe
                  // to key a list by") — the index joins the name for the same reason
                  // `AvatarDetailPanel`'s own face-grid key does.
                  <li
                    key={`${item.name}-${index}`}
                    className="flex items-center gap-1.5 rounded-[3px] border border-line bg-surface-2 px-2 py-1 text-[0.78rem] text-ink"
                  >
                    {clampPackDisplayText(item.name)}
                    {item.suggestedPersona !== null && (
                      <Chip>Suggested: {prettifySlug(item.suggestedPersona)}</Chip>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </li>
        );
      })}
    </ul>
  );
}
