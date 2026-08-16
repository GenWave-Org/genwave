"use client";

import { useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { readErrorMessage } from "@/lib/problem-details";

export interface IconUninstallPackButtonProps {
  /** The installed pack's slug — `DELETE /api/icon-packs/{slug}`'s route target (SPEC F130.5). */
  slug: string;
  /**
   * Whether this pack is the station's current `Station:IconPack` (SPEC F130.4) — widens the
   * confirm dialog's own copy with the fail-open note (STORY-337 AC6's "uninstall the active pack
   * fails open" — this is where an operator learns that BEFORE clicking Uninstall, not only after
   * on the Settings page's own dangling-value notice). The DELETE itself is IDENTICAL either way:
   * guard-free (SPEC F130.5, mirrors `AvatarUninstallPackButton`'s own remarks) — no referenced-by
   * check, no cross-store write to `station.settings`.
   */
  isActive: boolean;
}

/**
 * The Icons tab's per-pack uninstall affordance (SPEC F130.5, STORY-337, PLAN T304) — mirrors
 * `AvatarUninstallPackButton`'s own confirm/fetch/toast idiom exactly (that component's own remarks
 * explain why icon packs, like avatar packs, need no 409/referenced-by branch: `IconPackController.Uninstall`
 * is guard-free by design). The one addition is `isActive`'s own consequence-copy widening — an
 * icon pack's uninstall has a REAL, immediate visual consequence a font/avatar pack's own doesn't
 * (the whole admin chrome falls back to house icons the instant this DELETE lands), so the confirm
 * step names that plainly rather than leaving it a silent surprise.
 *
 * <b>204 → refresh, not local state surgery.</b> `wardrobe/page.tsx` is server-rendered
 * (`export const dynamic = "force-dynamic"`) — `router.refresh()` re-runs the server
 * `GET /api/icon-packs` fetch and re-renders with the pack gone, the SAME pattern
 * `UninstallPackButton`/`AvatarUninstallPackButton` use.
 */
export function IconUninstallPackButton({ slug, isActive }: IconUninstallPackButtonProps): ReactNode {
  const router = useRouter();
  const confirm = useConfirm();
  const [busy, setBusy] = useState(false);

  async function handleUninstall(): Promise<void> {
    const activeNote = isActive
      ? " This pack is the station's active icon set — the admin chrome falls back to house icons immediately; nothing errors."
      : "";
    const confirmed = await confirm({
      title: "Uninstall icon pack",
      consequence: `Uninstall "${slug}"?${activeNote}`,
      confirmLabel: "Uninstall",
      destructive: true,
    });
    if (!confirmed) return;

    setBusy(true);
    try {
      const resp = await fetch(`/api/icon-packs/${encodeURIComponent(slug)}`, { method: "DELETE" });

      if (resp.status === 204) {
        toast.success(`"${slug}" uninstalled.`);
        router.refresh();
        return;
      }

      toast.error(await readErrorMessage(resp));
    } catch {
      toast.error("Network error — check your connection");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Button
      type="button"
      variant="secondary"
      aria-label={`Uninstall ${slug}`}
      disabled={busy}
      onClick={() => {
        void handleUninstall();
      }}
    >
      Uninstall
    </Button>
  );
}
