"use client";

import type { ReactNode } from "react";
import { readErrorMessage } from "@/lib/problem-details";
import { CatalogInstallConfirmModal, type CatalogInstallOutcome } from "./CatalogInstallConfirmModal";

export interface IconInstallResult {
  iconCount: number;
}

interface IconPackInstallSuccessBody {
  slug: string;
  iconCount: number;
}

export interface IconInstallModalProps {
  /** The catalog entry's own slug — the install route's target AND upsert key
   * (`IconPackController.Install`, SPEC F130.5): a pack installs under the same slug it is known by
   * on the shelf, mirroring `AvatarInstallModal`'s own `slug` prop. */
  slug: string;
  onCancel: () => void;
  onInstalled: (result: IconInstallResult) => void;
}

/**
 * The icon pack catalog's install confirmation (SPEC F130.5, STORY-337, PLAN T304) — copy #4 onto
 * the shared `CatalogInstallConfirmModal` shell (the extraction this task's own rider names), the
 * SAME "no request body" shape `FontInstallModal`/`AvatarInstallModal` already use:
 * `IconPackController.Install` fetches every byte itself, server-side, re-validating the whole
 * whitelist gate — this modal's Confirm POSTs with no body at all. The "review" already happened
 * via `IconDetailPanel`'s own specimen row, drawn by the SAME safe renderer the admin chrome itself
 * uses once installed.
 */
export function IconInstallModal({ slug, onCancel, onInstalled }: IconInstallModalProps): ReactNode {
  async function handleConfirm(): Promise<CatalogInstallOutcome> {
    try {
      const resp = await fetch(`/api/icon-packs/${encodeURIComponent(slug)}/install`, { method: "POST" });

      if (resp.ok) {
        const body = (await resp.json()) as IconPackInstallSuccessBody;
        onInstalled({ iconCount: body.iconCount });
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
      ariaLabel="Install icon pack"
      testId="icon-install"
      description="The station fetches, re-validates, and stores this pack's icon set immediately. Nothing installs until you confirm."
      onCancel={onCancel}
      onConfirm={handleConfirm}
    />
  );
}
