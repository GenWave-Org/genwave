"use client";

import { useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { clampPackDisplayText } from "@/lib/clamp-pack-display-text";
import { readProblemDetails } from "@/lib/problem-details";

export interface InstallToggleProps {
  /** Catalog entry slug — the DELETE call targets `${deletePath}/{slug}`. */
  slug: string;
  /** Plain-words name for the confirm dialog and toasts (falls back to `prettifySlug(slug)`). */
  displayName: string;
  /** Whether a pack under this slug is installed (SPEC F204.1) — "Install" when `false`,
   * "Uninstall" when `true`; there is no third "Re-install" state. */
  isInstalled: boolean;
  /** This kind's own base route, e.g. `/api/voice-packs` — DELETE targets `${deletePath}/{slug}`. */
  deletePath: string;
  /** Lowercase kind name for the confirm dialog's title, e.g. "voice pack" → "Uninstall voice pack". */
  kindLabel: string;
  /** What one unit of this pack's content is called, for the confirm dialog's consequence sentence. */
  removedNoun: string;
  /** Opens the kind's own `*InstallModal.tsx` confirm step; this toggle issues no install request. */
  onInstallClick: () => void;
  /** Fires once the DELETE resolves as removed (2xx, or a 404 for an already-gone pack) — the
   * caller removes `slug` from its own locally-held installed set so the row flips without a reload. */
  onUninstalled: (slug: string) => void;
  /** Disables Install without hiding it (e.g. an unparsable manifest); never applies to Uninstall.
   * Defaults to `false`. */
  installDisabled?: boolean;
}

/** Wire shape of `AdPackController.Uninstall`'s 200 body (`AdPackUninstallResponse`) — the only
 * kind whose successful DELETE carries a body at all. Narrowed by hand, never cast. */
interface UninstallKeptSponsorsBody {
  keptSponsors?: unknown;
}

function narrowKeptSponsorNames(raw: unknown): string[] {
  if (typeof raw !== "object" || raw === null) return [];
  const sponsors = (raw as UninstallKeptSponsorsBody).keptSponsors;
  if (!Array.isArray(sponsors)) return [];
  return sponsors
    .map((entry) => (typeof entry === "object" && entry !== null ? (entry as { name?: unknown }).name : undefined))
    .filter((name): name is string => typeof name === "string" && name !== "")
    .map(clampPackDisplayText);
}

/** Success copy for a DELETE that landed 2xx — every kind's bare 204 just names the pack; an ad
 * pack's 200 also names any sponsor `AdPackController.Uninstall` kept (its briefs already gone). */
async function readUninstallSuccessMessage(resp: Response, displayName: string): Promise<string> {
  if (resp.status === 204) return `"${displayName}" uninstalled.`;
  const names = narrowKeptSponsorNames(await resp.json().catch(() => undefined));
  if (names.length === 0) return `"${displayName}" uninstalled.`;
  const word = names.length === 1 ? "sponsor" : "sponsors";
  return `"${displayName}" uninstalled. Kept ${word}: ${names.join(", ")}.`;
}

/** 409 copy: names the referrers off `referencedBy` (T563) when present, the raw `detail`
 * sentence otherwise. */
async function readUninstallFailureMessage(resp: Response): Promise<string> {
  const problem = await readProblemDetails(resp);
  if (problem.referencedBy !== undefined) {
    const names = problem.referencedBy.map(clampPackDisplayText);
    return `Cannot uninstall — still used by ${names.join(", ")}.`;
  }
  return problem.detail;
}

/**
 * Shared Install/Uninstall control for every pack-shaped catalog row (SPEC F204.1, STORY-472, PLAN
 * T564) — one button, not two; Uninstall confirms then DELETEs, Install bubbles `onInstallClick`
 * up unchanged (each kind's own `*InstallModal.tsx` still owns the POST).
 */
export function InstallToggle({
  slug,
  displayName,
  isInstalled,
  deletePath,
  kindLabel,
  removedNoun,
  onInstallClick,
  onUninstalled,
  installDisabled = false,
}: InstallToggleProps): ReactNode {
  const [uninstalling, setUninstalling] = useState(false);
  const router = useRouter();
  const confirm = useConfirm();

  async function handleUninstall(): Promise<void> {
    const confirmed = await confirm({
      title: `Uninstall ${kindLabel}`,
      consequence: `Uninstall "${displayName}"? Every ${removedNoun} it added is removed from this station immediately.`,
      confirmLabel: "Uninstall",
      destructive: true,
    });
    if (!confirmed) return;

    setUninstalling(true);
    try {
      const resp = await fetch(`${deletePath}/${encodeURIComponent(slug)}`, { method: "DELETE" });
      if (resp.ok) {
        toast.success(await readUninstallSuccessMessage(resp, displayName));
        onUninstalled(slug);
        router.refresh();
      } else if (resp.status === 404) {
        // Already gone (uninstalled elsewhere) — a stale local flag pointing at a deleted pack must
        // self-heal, not keep offering an "Uninstall" that can only 404 again.
        toast.success(`"${displayName}" was already uninstalled.`);
        onUninstalled(slug);
        router.refresh();
      } else {
        toast.error(await readUninstallFailureMessage(resp));
      }
    } catch {
      toast.error("Network error — check your connection");
    } finally {
      setUninstalling(false);
    }
  }

  if (isInstalled) {
    return (
      <Button
        type="button"
        variant="secondary"
        disabled={uninstalling}
        onClick={() => {
          void handleUninstall();
        }}
      >
        Uninstall
      </Button>
    );
  }

  return (
    <Button type="button" variant="primary" onClick={onInstallClick} disabled={installDisabled}>
      Install
    </Button>
  );
}
