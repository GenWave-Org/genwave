"use client";

import type { ReactNode } from "react";
import { readErrorMessage } from "@/lib/problem-details";
import { CatalogInstallConfirmModal, type CatalogInstallOutcome } from "./CatalogInstallConfirmModal";

export interface FontInstallResult {
  family: string;
}

interface FontPackInstallSuccessBody {
  slug: string;
  family: string;
  faces: string[];
  importedFrom: string;
}

export interface FontInstallModalProps {
  /** The catalog entry's own slug — the install route's target AND upsert key
   * (`FontPackController.Install`, SPEC F104.5): a pack installs under the same slug it is known
   * by on the shelf. No separate provenance parameter (unlike `ThemeInstallModal`'s own
   * `?catalogSlug=`) — a font pack has no other install path a provenance stamp would need to
   * disambiguate from (SPEC F104.5: "packs have no file-upload or authored path"). */
  slug: string;
  onCancel: () => void;
  onInstalled: (result: FontInstallResult) => void;
}

/**
 * The font catalog's install confirmation (SPEC F104.5, STORY-282, PLAN T202 — a scope addition,
 * see `FontDetailPanel`'s own remarks for why this task builds it; re-platformed onto the shared
 * `CatalogInstallConfirmModal` shell at PLAN T304) — the "review" already happened via
 * `SpecimenBlock`'s own real, hash-verified face showing behind this modal, not a manifest this
 * dialog would otherwise have to re-echo.
 *
 * <b>No request body</b> (unlike `ThemeInstallModal`'s POSTed manifest text): `FontPackController.Install`
 * takes ONLY the route slug and fetches every byte itself, server-side, through the guarded door
 * (SPEC F104.5's own "no request body, by design" rule) — this modal's Confirm POSTs with no body
 * at all.
 */
export function FontInstallModal({ slug, onCancel, onInstalled }: FontInstallModalProps): ReactNode {
  async function handleConfirm(): Promise<CatalogInstallOutcome> {
    try {
      const resp = await fetch(`/api/fonts/${encodeURIComponent(slug)}/install`, { method: "POST" });

      if (resp.ok) {
        const body = (await resp.json()) as FontPackInstallSuccessBody;
        onInstalled({ family: body.family });
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
      ariaLabel="Install font pack"
      testId="font-install"
      description="The station fetches and stores this pack's faces immediately. Nothing installs until you confirm."
      onCancel={onCancel}
      onConfirm={handleConfirm}
    />
  );
}
