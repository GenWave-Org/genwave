"use client";

import type { ReactNode } from "react";
import { Chip } from "@/components/ui/chip";
import { parseIconPackDefinition } from "@/lib/icon-pack";
import { IconPackSpecimenRow } from "../_components/IconPackRenderer";
import { BestForChips, MatureBadge } from "./catalog-badges";
import { prettifySlug } from "./format-slug";
import { InstallToggle } from "./InstallToggle";
import type { CatalogEntryDetailDto } from "./types";

export interface IconDetailPanelProps {
  slug: string;
  detail: CatalogEntryDetailDto;
  /** Whether THIS slug already has an installed pack — mirrors `AvatarDetailPanel`'s own
   * `isInstalled` prop. Sourced from `GET /api/icon-packs`'s own listing (see
   * `PersonaCatalogClient`'s own remarks for where that read happens and how a fresh install flips
   * this without a reload). */
  isInstalled: boolean;
  onInstallClick: () => void;
  /** Fires once `DELETE /api/icon-packs/{slug}` resolves as removed (2xx, or 404 for an
   * already-gone pack) (SPEC F204.2, PLAN T564) — the caller removes this slug from its own
   * installed set so the row flips without a reload. */
  onUninstalled: (slug: string) => void;
}

/**
 * An icon pack entry's detail view (SPEC F130.1, F130.6, STORY-337, PLAN T304) — mirrors
 * `AvatarDetailPanel`'s own shape one level up (name, 18+ badge, an Install/Re-install button that
 * opens a confirm modal rather than posting anything itself, an "Installed" chip once a pack under
 * this slug is already installed) with the specimen half replaced by a SPECIMEN ROW: every
 * icon `detail.card`'s own definition declares, drawn small through the SAME safe renderer
 * (`IconPackSpecimenRow`/`IconPackGlyph`) the active admin chrome itself uses once a pack installs.
 *
 * No pack-name line (unlike `AvatarDetailPanel`'s own `packName` heading fallback) — SPEC F130.1's
 * `gw-icon-pack` document has no pack-level display name field at all; the heading falls back to
 * `prettifySlug(slug)` unconditionally, the same fallback every kind uses when it has no separate
 * display name. `detail.iconCount` (PLAN T304 rider 4, parsed server-side at zero extra cost) names
 * the pack's own declared count as a small kicker line; the specimen row itself is drawn straight
 * from `detail.card` via THIS component's own defensive parse (`parseIconPackDefinition`) — nothing
 * client-side ever trusts a pre-install manifest structurally, even though the server already
 * re-validated it once to produce `iconCount` (PLAN T304 rider 1's own "defensive regardless"
 * ruling).
 */
export function IconDetailPanel({ slug, detail, isInstalled, onInstallClick, onUninstalled }: IconDetailPanelProps): ReactNode {
  const definition = detail.card === null ? null : parseIconPackDefinition(detail.card);

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
        {/* Install opens IconInstallModal's confirm step — this click itself issues no request; the
            modal POSTs on confirm only, no request body (mirrors AvatarInstallModal's own "no
            request body, by design" rule — IconPackController.Install fetches every byte server-side
            too). Uninstall calls `DELETE /api/icon-packs/{slug}` directly (SPEC F204.1/F204.2, PLAN
            T564 — icon uninstall never 409, guard-free by design). */}
        <InstallToggle
          slug={slug}
          displayName={prettifySlug(slug)}
          isInstalled={isInstalled}
          deletePath="/api/icon-packs"
          kindLabel="icon pack"
          removedNoun="icon"
          onInstallClick={onInstallClick}
          onUninstalled={onUninstalled}
        />
      </div>

      <BestForChips items={detail.bestFor ?? []} />

      {/* Plain text ONLY (mirrors DetailPanel's own persona-description rule, SPEC F90.6) — a bare
          `{detail.description}` JSX child, React's default escaping, never dangerouslySetInnerHTML. */}
      {detail.description !== null && detail.description !== "" && (
        <p className="text-[0.85rem] text-ink">{detail.description}</p>
      )}

      {detail.iconCount !== null && (
        <p className="text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
          {detail.iconCount} icon{detail.iconCount === 1 ? "" : "s"}
        </p>
      )}

      {definition === null ? (
        <p className="text-[0.85rem] text-mute">This pack&apos;s definition could not be previewed.</p>
      ) : (
        <IconPackSpecimenRow definition={definition} />
      )}

      <p className="text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
        Transient preview — browsing installs nothing
      </p>
    </div>
  );
}
