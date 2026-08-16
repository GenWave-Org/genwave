"use client";

import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { AvatarItemFace } from "./AvatarItemFace";
import { BestForChips, MatureBadge } from "./catalog-badges";
import { prettifySlug } from "./format-slug";
import type { CatalogEntryDetailDto } from "./types";

export interface AvatarDetailPanelProps {
  slug: string;
  detail: CatalogEntryDetailDto;
  /** Whether THIS slug already has an installed pack — mirrors `FontDetailPanel`'s own
   * `isInstalled` prop (PLAN T204 precedent, applied here per this task's own "match the font
   * install flow exactly" instruction). Sourced from `GET /api/avatar-packs`'s own listing (see
   * `PersonaCatalogClient`'s own remarks for where that read happens and how a fresh install flips
   * this without a reload). */
  isInstalled: boolean;
  onInstallClick: () => void;
}

/**
 * An avatar pack entry's detail view (SPEC F128.1, F128.4, PLAN T294) — mirrors `FontDetailPanel`'s
 * own shape one level up (name, 18+ badge, an Install/Re-install button that opens a confirm modal
 * rather than posting anything itself, an "Installed" chip once a pack under this slug is already in
 * the Wardrobe) with the specimen half replaced by a FACE GRID: every `detail.avatarItems` entry
 * (SPEC F128.1's `items[]`, parsed off the already-fetched `.avatar.json` manifest at zero extra
 * network cost, T292) rendered as its own `AvatarItemFace` tile — see that component's own remarks
 * for why it loads through a plain `<img>` rather than `SpecimenBlock`'s own fetch/Blob/FontFace
 * machinery.
 *
 * No pack-name/item-count line here (a stated deviation from this task's own dispatch wording,
 * "showing pack name, item count"): `CatalogShelfEntryDto`/`CatalogEntryResponse` carry no
 * avatar-kind display-name or item-count field of their own — widening either wire shape is T292's
 * own territory, already shipped and out of scope here (T294's own "smallest honest surface" rule
 * applied the same way `FontShelfCard`'s own remarks apply it to a font pack's shelf card: this panel
 * shows exactly what the entry's OWN fetched fields carry). The heading falls back to
 * `prettifySlug(slug)`, the SAME fallback every other kind's card/panel already uses when no
 * separate display name exists on the wire; the item COUNT is implicit in the face grid itself
 * (`detail.avatarItems.length` tiles, rendered), never restated as a separate line.
 */
export function AvatarDetailPanel({ slug, detail, isInstalled, onInstallClick }: AvatarDetailPanelProps): ReactNode {
  const items = detail.avatarItems ?? [];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="font-display text-[1.1rem] text-ink">{prettifySlug(slug)}</h2>
          {/* 18+ badge — ALWAYS shown on a mature entry, never behind a toggle (the house rule this
              task's own dispatch restates). */}
          {detail.audience === "mature" && <MatureBadge />}
          {isInstalled && <Chip>Installed</Chip>}
        </div>
        {/* Install/Re-install opens AvatarInstallModal's confirm step — this click itself issues no
            request; the modal POSTs on confirm only, no request body (mirrors FontInstallModal's own
            "no request body, by design" rule — AvatarPackController.Install fetches every byte
            server-side too). */}
        <Button type="button" variant="primary" onClick={onInstallClick}>
          {isInstalled ? "Re-install" : "Install"}
        </Button>
      </div>

      <BestForChips items={detail.bestFor ?? []} />

      {/* Plain text ONLY (mirrors DetailPanel's own persona-description rule, SPEC F90.6) — a bare
          `{detail.description}` JSX child, React's default escaping, never dangerouslySetInnerHTML. */}
      {detail.description !== null && detail.description !== "" && (
        <p className="text-[0.85rem] text-ink">{detail.description}</p>
      )}

      {items.length === 0 ? (
        <p className="text-[0.85rem] text-mute">This pack declares no items.</p>
      ) : (
        <ul aria-label="Pack faces" className="grid grid-cols-3 gap-2 sm:grid-cols-4">
          {items.map((item, index) => (
            <AvatarItemFace
              // Item names are NOT guaranteed unique on this pre-install, unvalidated-by-length read
              // (unlike the install route's own store-level UNIQUE(pack_id, name) constraint) — the
              // index is part of the key so two identically-named items never collide as React keys.
              key={`${item.name}-${index}`}
              slug={slug}
              name={item.name}
              file={item.file}
              suggestedPersona={item.suggestedPersona}
            />
          ))}
        </ul>
      )}

      <p className="text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
        Transient preview — browsing installs nothing
      </p>
    </div>
  );
}
