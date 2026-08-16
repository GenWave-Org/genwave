"use client";

import type { ReactNode } from "react";
import { readErrorMessage } from "@/lib/problem-details";
import { CatalogInstallConfirmModal, type CatalogInstallOutcome } from "./CatalogInstallConfirmModal";

export interface AvatarInstallResult {
  packName: string;
}

interface AvatarPackInstallSuccessBody {
  slug: string;
  packName: string;
  items: string[];
  importedFrom: string;
}

export interface AvatarInstallModalProps {
  /** The catalog entry's own slug — the install route's target AND upsert key
   * (`AvatarPackController.Install`, SPEC F128.3): a pack installs under the same slug it is known
   * by on the shelf, mirroring `FontInstallModal`'s own `slug` prop. */
  slug: string;
  onCancel: () => void;
  onInstalled: (result: AvatarInstallResult) => void;
}

/**
 * The avatar pack catalog's install confirmation (SPEC F128.3, STORY-332, PLAN T294; re-platformed
 * onto the shared `CatalogInstallConfirmModal` shell at PLAN T304) — mirrors `FontInstallModal`'s
 * own confirm/cancel semantics exactly (this task's own instruction: "match however FONT pack
 * install confirms"). `AvatarPackController.Install` takes no request body — every byte is fetched,
 * re-validated, and normalized server-side through the guarded door — so Confirm POSTs with no body
 * at all, the SAME shape `FontInstallModal`'s own Confirm uses. The "review" already happened via
 * `AvatarDetailPanel`'s own face grid, real hash-verified previews shown behind this modal.
 */
export function AvatarInstallModal({ slug, onCancel, onInstalled }: AvatarInstallModalProps): ReactNode {
  async function handleConfirm(): Promise<CatalogInstallOutcome> {
    try {
      const resp = await fetch(`/api/avatar-packs/${encodeURIComponent(slug)}/install`, { method: "POST" });

      if (resp.ok) {
        const body = (await resp.json()) as AvatarPackInstallSuccessBody;
        onInstalled({ packName: body.packName });
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
      ariaLabel="Install avatar pack"
      testId="avatar-install"
      description="The station fetches, re-validates, and stores this pack's faces immediately. Nothing installs until you confirm."
      onCancel={onCancel}
      onConfirm={handleConfirm}
    />
  );
}
