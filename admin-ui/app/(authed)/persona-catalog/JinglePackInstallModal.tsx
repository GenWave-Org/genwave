"use client";

import type { ReactNode } from "react";
import { toast } from "@/components/ui/toast";
import { readErrorMessage } from "@/lib/problem-details";
import { CatalogInstallConfirmModal, type CatalogInstallOutcome } from "./CatalogInstallConfirmModal";

export interface JinglePackInstallResult {
  packName: string;
  assetCount: number;
}

interface JinglePackInstallSuccessBody {
  slug: string;
  packName: string;
  assets: unknown[];
}

export interface JinglePackInstallModalProps {
  /** The catalog entry's own slug — `POST /api/jingle-packs/{slug}/install`'s route target. */
  slug: string;
  onCancel: () => void;
  onInstalled: (result: JinglePackInstallResult) => void;
}

/**
 * The jingle-pack catalog's install confirmation (SPEC F165, STORY-397, PLAN T418) — copy #7 onto
 * the shared `CatalogInstallConfirmModal` shell, the same "no request body" shape every sibling
 * kind's install modal already uses: `JinglePackController.Install` fetches every declared asset
 * itself, server-side, measures and enriches each one, and moves the whole set into place in one DB
 * transaction — this modal's Confirm POSTs with no body at all. The review step already happened via
 * `JinglePackDetailPanel`'s own read-only asset table.
 *
 * A failed confirm toasts the problem's DETAIL (R5's own ruling — unlike the voice-pack sibling
 * modal's title-first read, `jingle_pack_in_use`'s own detail already names the referencing spots,
 * the more useful string here) via the shared `readErrorMessage`, and shows the same string inline.
 */
export function JinglePackInstallModal({ slug, onCancel, onInstalled }: JinglePackInstallModalProps): ReactNode {
  async function handleConfirm(): Promise<CatalogInstallOutcome> {
    try {
      const resp = await fetch(`/api/jingle-packs/${encodeURIComponent(slug)}/install`, { method: "POST" });

      if (resp.ok) {
        const body = (await resp.json()) as JinglePackInstallSuccessBody;
        onInstalled({ packName: body.packName, assetCount: body.assets.length });
        return { ok: true };
      }

      const message = await readErrorMessage(resp);
      toast.error(message);
      return { ok: false, message };
    } catch {
      return { ok: false, message: "Network error — check your connection" };
    }
  }

  return (
    <CatalogInstallConfirmModal
      slug={slug}
      ariaLabel="Install jingle pack"
      testId="jingle-pack-install"
      description="The station fetches, measures, and stores this pack's audio immediately. Nothing installs until you confirm."
      onCancel={onCancel}
      onConfirm={handleConfirm}
    />
  );
}
