"use client";

import type { ReactNode } from "react";
import { readErrorMessage } from "@/lib/problem-details";
import { CatalogInstallConfirmModal, type CatalogInstallOutcome } from "./CatalogInstallConfirmModal";

export interface AdPackInstallResult {
  packName: string | null;
  brands: string[];
}

interface AdPackInstallSuccessBody {
  slug: string;
  packName: string | null;
  brands: string[];
}

export interface AdPackInstallModalProps {
  /** The catalog entry's own slug — the install route's target AND upsert key
   * (`AdPackController.Install`, SPEC F162.2): a pack installs under the same slug it is known by on
   * the shelf, mirroring `IconInstallModal`'s own `slug` prop. */
  slug: string;
  onCancel: () => void;
  onInstalled: (result: AdPackInstallResult) => void;
}

/**
 * The ad-pack catalog's install confirmation (SPEC F162.2, STORY-393, PLAN T405) — copy #5 onto the
 * shared `CatalogInstallConfirmModal` shell, the SAME "no request body" shape
 * `FontInstallModal`/`AvatarInstallModal`/`IconInstallModal` already use: `AdPackController.Install`
 * fetches every byte itself, server-side, and upserts every declared brief into `station.ad_brief` —
 * this modal's Confirm POSTs with no body at all. The "review" already happened via
 * `AdPackDetailPanel`'s own read-only brief list.
 *
 * The description names BOTH halves of a reinstall's own contract (T405 review F5 — the RULED
 * PRESERVE semantics, honestly stated rather than left implicit): content — premise/tone/structure —
 * refreshes to whatever the pack currently declares, while a brief the operator has disabled STAYS
 * disabled (`IAdBriefStore.UpsertAsync`/`UpsertAllAsync`'s own PRESERVE-on-conflict ruling). Dean's
 * copy rule: every sentence starts with a capital.
 */
export function AdPackInstallModal({ slug, onCancel, onInstalled }: AdPackInstallModalProps): ReactNode {
  async function handleConfirm(): Promise<CatalogInstallOutcome> {
    try {
      const resp = await fetch(`/api/ad-packs/${encodeURIComponent(slug)}/install`, { method: "POST" });

      if (resp.ok) {
        const body = (await resp.json()) as AdPackInstallSuccessBody;
        onInstalled({ packName: body.packName, brands: body.brands });
        return { ok: true };
      }

      return { ok: false, message: await readErrorMessage(resp) };
    } catch {
      return { ok: false, message: "Network error — check your connection" };
    }
  }

  return (
    <CatalogInstallConfirmModal
      slug={slug}
      ariaLabel="Install ad pack"
      testId="ad-pack-install"
      description="The station fetches this pack's brand briefs and adds them to your Briefs tab immediately — no script, no audio, no code. Reinstalling refreshes each brief's premise, tone, and structure text; a brief you've disabled stays disabled. Nothing installs until you confirm."
      onCancel={onCancel}
      onConfirm={handleConfirm}
    />
  );
}
