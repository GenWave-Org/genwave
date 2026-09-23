"use client";

import type { ReactNode } from "react";
import { Chip } from "@/components/ui/chip";
import { clampPackDisplayText } from "@/lib/clamp-pack-display-text";
import { BestForChips, MatureBadge } from "./catalog-badges";
import { prettifySlug } from "./format-slug";
import { InstallToggle } from "./InstallToggle";
import type { CatalogAdPackBriefDto, CatalogEntryDetailDto } from "./types";

export interface AdPackDetailPanelProps {
  slug: string;
  detail: CatalogEntryDetailDto;
  /** Whether this slug already has an installed pack — sourced from `GET /api/ad-briefs`'s own
   * distinct `packSlug` values (no dedicated listing route exists, see `page.tsx`'s own
   * `fetchInstalledAdPackSlugs` remarks). */
  isInstalled: boolean;
  onInstallClick: () => void;
  /** Fires once the DELETE resolves as removed (2xx or 404) — the caller removes this slug from its
   * own installed set so the row flips without a reload. */
  onUninstalled: (slug: string) => void;
}

/**
 * An ad-pack entry's detail view (SPEC F162.2, STORY-393, PLAN T405) — name, 18+ badge, a read-only
 * brief list (`detail.adPackBriefs`, plain text only), and the shared `InstallToggle` (SPEC F204.1,
 * PLAN T564). Install is disabled when the manifest failed to parse (F6) — the route would 400 on
 * the same manifest.
 */
export function AdPackDetailPanel({ slug, detail, isInstalled, onInstallClick, onUninstalled }: AdPackDetailPanelProps): ReactNode {
  const briefs = detail.adPackBriefs;
  const parsed = briefs !== null;
  const displayName = clampPackDisplayText(detail.packName ?? prettifySlug(slug));

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="font-display text-[1.1rem] text-ink">{displayName}</h2>
          {/* 18+ badge — ALWAYS shown on a mature entry, never behind a toggle (the house rule this
              task's own dispatch restates). */}
          {detail.audience === "mature" && <MatureBadge />}
          {isInstalled && <Chip>Installed</Chip>}
        </div>
        {/* Install opens AdPackInstallModal's confirm step, no request body. Disabled when the
            manifest failed to parse (F6). Uninstall DELETEs; a 409 (still referenced) keeps the row
            on "Uninstall" (SPEC F204.3). */}
        <InstallToggle
          slug={slug}
          displayName={displayName}
          isInstalled={isInstalled}
          deletePath="/api/ad-packs"
          kindLabel="ad pack"
          removedNoun="brief"
          onInstallClick={onInstallClick}
          onUninstalled={onUninstalled}
          installDisabled={!parsed}
        />
      </div>

      <BestForChips items={detail.bestFor ?? []} />

      {/* Plain text ONLY (mirrors DetailPanel's own persona-description rule, SPEC F90.6) — a bare
          `{detail.description}` JSX child, React's default escaping, never dangerouslySetInnerHTML. */}
      {detail.description !== null && detail.description !== "" && (
        <p className="text-[0.85rem] text-ink">{detail.description}</p>
      )}

      {!parsed ? (
        <p role="alert" className="text-[0.85rem] text-danger">
          This pack&apos;s manifest could not be read — installing is disabled until the catalog
          serves a valid one.
        </p>
      ) : briefs.length === 0 ? (
        <p className="text-[0.85rem] text-mute">This pack declares no briefs.</p>
      ) : (
        <ul aria-label="Ad pack briefs" className="flex list-none flex-col gap-2 p-0">
          {briefs.map((brief, index) => (
            // Brand is NOT guaranteed unique on this pre-install, unvalidated-by-uniqueness read
            // (station.ad_brief's own UNIQUE constraint is a WRITE-time guarantee, not a manifest
            // shape one) — the index is part of the key so two identically-named briefs never
            // collide as React keys.
            <AdPackBriefRow key={`${brief.brand}-${index}`} brief={brief} />
          ))}
        </ul>
      )}

      {parsed && (
        <p className="text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
          Data only — no script, no audio, no code. Reviewing installs nothing.
        </p>
      )}
    </div>
  );
}

/** One brief's own read-only row — brand always shown, the three optional hints only when present
 * (an absent hint renders nothing, never a blank "Tone:" line). */
function AdPackBriefRow({ brief }: { brief: CatalogAdPackBriefDto }): ReactNode {
  // Computed FIRST (T405 review F10 — the prior shape gated on a SEPARATE `!== null` check that
  // disagreed with this join's own `!== ""` filter: a tone of `""` with a null structure passed the
  // gate but joined to an empty string, rendering a visibly blank line). Gating on the COMPUTED
  // string itself is the one check that can never disagree with what actually renders.
  const hints = [brief.tone, brief.structure].filter((value): value is string => value !== null && value !== "").join(" · ");

  return (
    <li className="rounded-[6px] border border-line bg-surface-2 px-3 py-2 text-[0.85rem] text-ink">
      <p className="font-display text-[0.95rem]">{brief.brand}</p>
      {brief.premise !== null && brief.premise !== "" && <p className="mt-1 text-mute">{brief.premise}</p>}
      {hints !== "" && <p className="mt-1 text-[0.75rem] text-mute">{hints}</p>}
    </li>
  );
}
