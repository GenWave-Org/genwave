"use client";

import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { clampPackDisplayText } from "@/lib/clamp-pack-display-text";
import { BestForChips, MatureBadge } from "./catalog-badges";
import { prettifySlug } from "./format-slug";
import type { CatalogAdPackBriefDto, CatalogEntryDetailDto } from "./types";

export interface AdPackDetailPanelProps {
  slug: string;
  detail: CatalogEntryDetailDto;
  onInstallClick: () => void;
}

/**
 * An ad-pack entry's detail view (SPEC F162.2, STORY-393, PLAN T405) — mirrors `IconDetailPanel`'s
 * own shape one kind over (name, 18+ badge, an Install button that opens a confirm modal rather than
 * posting anything itself) with the specimen half replaced by a READ-ONLY BRIEF LIST: every
 * `detail.adPackBriefs` entry (SPEC F162.2's own `briefs[]`, parsed off the already-fetched
 * `.ad-pack.json` manifest at zero extra network cost) rendered as brand/premise/tone/structure —
 * plain text only, the SAME `DetailPanel`/`AvatarDetailPanel` rule (SPEC F90.6): nothing here is
 * interpreted, and nothing here is editable — a brief's own editable home is the Ads page's Briefs
 * tab (SPEC F162.1), reached only AFTER an explicit install.
 *
 * The heading reads `detail.packName ?? prettifySlug(slug)` (mirrors `AvatarDetailPanel`'s own
 * fallback) — SPEC F162.2's own "pack metadata" leaves `packName` genuinely optional on this kind
 * (unlike an avatar pack's own required one), so a pack with none still gets an honest, slug-derived
 * title rather than a blank heading.
 *
 * PARSED-EMPTY vs UNPARSEABLE (T405 review F6 — this panel used to conflate the two): `null` means
 * the manifest could NOT be read (a hostile/malformed pack, or the catalog was unreachable) —
 * `AdPackController.Install` would 400 on the SAME manifest, so this panel must not contradict that
 * by offering an Install button that can only fail; Install is DISABLED and the panel names the
 * degrade instead. `[]` (a genuinely empty, but successfully PARSED, brief list — never actually
 * reachable off the real wire today, since `CatalogAdPackManifestSerializer.Deserialize` itself
 * refuses a briefless manifest, but the wire TYPE still allows it and this panel stays honest to it
 * defensively, the "never trust the wire blindly" idiom this codebase already holds every other
 * safe renderer to) keeps Install enabled and simply names the pack as declaring none.
 *
 * NO "Installed"/"Re-install" state on this panel (T405's own deliberate, stated scope line — unlike
 * every sibling pack kind, this route's installed state lives INSIDE `station.ad_brief`, mixed with
 * owner-authored rows, with no dedicated per-pack listing endpoint this task adds — see
 * `AdPackController`'s own class remarks for the full reasoning): the button always reads "Install",
 * even on a slug already installed — a legitimate, idempotent action either way (SPEC F162.2's own
 * upsert contract), never a destructive one. The station's own Briefs tab (`GET /api/ad-briefs`,
 * already shipped) is where an operator actually confirms what landed.
 */
export function AdPackDetailPanel({ slug, detail, onInstallClick }: AdPackDetailPanelProps): ReactNode {
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
        </div>
        {/* Install opens AdPackInstallModal's confirm step — this click itself issues no request;
            the modal POSTs on confirm only, no request body (mirrors IconInstallModal's own "no
            request body, by design" rule — AdPackController.Install fetches every byte server-side
            too). Disabled when the manifest failed to parse (F6): the route would 400 on the exact
            same manifest, so this panel must never offer an Install that can only fail. */}
        <Button type="button" variant="primary" onClick={onInstallClick} disabled={!parsed}>
          Install
        </Button>
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
