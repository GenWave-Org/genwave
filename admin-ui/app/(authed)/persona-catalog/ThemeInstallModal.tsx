"use client";

import type { ReactNode } from "react";
import { readErrorMessage } from "@/lib/problem-details";
import { CatalogInstallConfirmModal, type CatalogInstallOutcome } from "./CatalogInstallConfirmModal";

export interface ThemeInstallResult {
  name: string;
  /** The provenance stamp the import route actually wrote (SPEC F103.11) — always this modal's own
   * `slug` in practice (see `ThemeCatalogProvenanceDto`'s own remarks), read off the response
   * rather than assumed, mirroring `ThemeImportSuccessBody`'s own already-present field. */
  importedFrom: string;
  /** When {@link importedFrom} was stamped (gh-#375) — a server read-back
   * (`ThemesImportController`'s own remarks), never a client-side `Date.now()` guess, so
   * `PersonaCatalogClient`'s post-install local flip can show the SAME provenance line a fresh
   * `GET /api/settings` read would. */
  importedAt: string;
}

interface ThemeImportSuccessBody {
  slug: string;
  name: string;
  importedFrom: string;
  importedAt: string;
}

export interface ThemeInstallModalProps {
  /** The catalog entry's own slug — used as BOTH the install route's target slug and the
   * `?catalogSlug=` provenance value (SPEC F90.7's persona precedent, applied to the theme kind by
   * PLAN T186: a catalog theme installs under the same slug it is known by on the shelf). */
  slug: string;
  /** The raw, already hash-verified theme manifest JSON text (SPEC F90.3) — the SAME bytes
   * `ThemeDetailPreview` already composed a preview from; POSTed byte-for-byte on confirm, never
   * re-derived or re-fetched. */
  manifestText: string;
  onCancel: () => void;
  onInstalled: (result: ThemeInstallResult) => void;
}

/**
 * The theme catalog's install confirmation (SPEC F103.6, STORY-274, PLAN T186; re-platformed onto
 * the shared `CatalogInstallConfirmModal` shell at PLAN T304 — this file now owns only the theme
 * kind's own POST target/body and success-body mapping, mechanically unchanged behaviour). The
 * "review" already happened via the live composed preview showing behind this modal
 * (`ThemeDetailPreview`) — unlike `PersonaCardReviewModal`, this dialog does not re-render the
 * manifest's own fields, it only asks for the final go/no-go.
 */
export function ThemeInstallModal({ slug, manifestText, onCancel, onInstalled }: ThemeInstallModalProps): ReactNode {
  async function handleConfirm(): Promise<CatalogInstallOutcome> {
    const encodedSlug = encodeURIComponent(slug);

    try {
      const resp = await fetch(`/api/themes/${encodedSlug}/import?catalogSlug=${encodedSlug}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: manifestText,
      });

      if (resp.ok) {
        const body = (await resp.json()) as ThemeImportSuccessBody;
        onInstalled({ name: body.name, importedFrom: body.importedFrom, importedAt: body.importedAt });
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
      ariaLabel="Install theme"
      testId="theme-install"
      description="The station adopts this theme immediately for anyone who selects it. Nothing installs until you confirm."
      onCancel={onCancel}
      onConfirm={handleConfirm}
    />
  );
}
