"use client";

import type { ReactNode } from "react";
import { toast } from "@/components/ui/toast";
import { CatalogInstallConfirmModal, type CatalogInstallOutcome } from "./CatalogInstallConfirmModal";

export interface VoicePackInstallResult {
  packName: string;
  voiceIds: string[];
}

interface VoicePackInstallSuccessBody {
  slug: string;
  packName: string;
  voiceIds: string[];
}

interface ProblemDetailsTitleBody {
  title?: string;
  detail?: string;
}

function isProblemDetailsTitleBody(raw: unknown): raw is ProblemDetailsTitleBody {
  return typeof raw === "object" && raw !== null;
}

/**
 * Reads the ProblemDetails `title` first, falling back to `detail`, then a generic message —
 * mirrors `PersonaPreview.tsx`'s own `readPreviewFailureMessage` exactly (SPEC F35.7's own
 * "toast with the ProblemDetails title" precedent), rather than `lib/problem-details.ts`'s shared
 * `readErrorMessage`/`readProblemDetails` (detail-first, no `title` at all): R4's ruling wants THIS
 * kind's own install conflicts (`VoiceIdCollisionProblem`'s short, actionable title, e.g. "Voice id
 * already installed.") over the longer per-id detail sentence a toast has no room for.
 */
async function readInstallFailureTitle(resp: Response): Promise<string> {
  try {
    const raw = (await resp.json()) as unknown;
    if (isProblemDetailsTitleBody(raw)) {
      if (typeof raw.title === "string" && raw.title !== "") return raw.title;
      if (typeof raw.detail === "string" && raw.detail !== "") return raw.detail;
    }
  } catch {
    // malformed or empty body — fall through to the generic message
  }
  return `Unexpected error (${resp.status})`;
}

export interface VoicePackInstallModalProps {
  /** The catalog entry's own slug — `POST /api/voice-packs/{slug}/install`'s route target. */
  slug: string;
  onCancel: () => void;
  onInstalled: (result: VoicePackInstallResult) => void;
}

/**
 * The voice-pack catalog's install confirmation (SPEC F164, STORY-397, PLAN T418) — copy #6 onto the
 * shared `CatalogInstallConfirmModal` shell, the same "no request body" shape every sibling kind's
 * install modal already uses (`AdPackInstallModal`/`IconInstallModal`/…): `VoicePackController.Install`
 * fetches every declared file itself, server-side (the preview clip plus one `.pt` per voice) — this
 * modal's Confirm POSTs with no body at all. The review step already happened via
 * `VoicePackDetailPanel`'s own honest-preview gate (F103.5/STORY-397 AC4): this dialog can only ever
 * open once that panel has already proven the preview plays.
 *
 * A failed confirm both toasts AND shows inline the SAME string — the problem's title, not its
 * detail (R4's own ruling, {@link readInstallFailureTitle}'s own remarks) — unlike every other
 * install modal on this surface, which shows an inline-only detail message with no toast at all.
 */
export function VoicePackInstallModal({ slug, onCancel, onInstalled }: VoicePackInstallModalProps): ReactNode {
  async function handleConfirm(): Promise<CatalogInstallOutcome> {
    try {
      const resp = await fetch(`/api/voice-packs/${encodeURIComponent(slug)}/install`, { method: "POST" });

      if (resp.ok) {
        const body = (await resp.json()) as VoicePackInstallSuccessBody;
        onInstalled({ packName: body.packName, voiceIds: body.voiceIds });
        return { ok: true };
      }

      const title = await readInstallFailureTitle(resp);
      toast.error(title);
      return { ok: false, message: title };
    } catch {
      return { ok: false, message: "Network error — check your connection" };
    }
  }

  return (
    <CatalogInstallConfirmModal
      slug={slug}
      ariaLabel="Install voice pack"
      testId="voice-pack-install"
      description="The station fetches this pack's voices and preview clip immediately. Nothing installs until you confirm."
      onCancel={onCancel}
      onConfirm={handleConfirm}
    />
  );
}
