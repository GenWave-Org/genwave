"use client";

import { useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { readErrorMessage } from "@/lib/problem-details";

export interface AvatarUninstallPackButtonProps {
  /** The installed pack's slug — `DELETE /api/avatar-packs/{slug}`'s route target (SPEC F128.3). */
  slug: string;
  /** The pack's display name (already clamped by the caller — see `AvatarWardrobeClient`'s own
   * remarks) — used in the confirm dialog and toast copy only, never in the request itself (the
   * route is keyed on `slug`, mirroring `UninstallPackButton`'s own split). */
  displayName: string;
}

/**
 * The Avatars tab's per-pack uninstall affordance (SPEC F128.3, F128.5, PLAN T294) — mirrors
 * `UninstallPackButton`'s own confirm/fetch/toast idiom (the font tab precedent this task's own
 * dispatch names), with ONE deliberate simplification: no 409/referenced-by branch.
 * `AvatarPackController.Uninstall` is GUARD-FREE by design (that controller's own remarks: a worn
 * face already applied to a persona is a COPY of a pack item's bytes at the moment it was applied,
 * never a live reference into `station.avatar_pack_item` — the exact opposite of a saved theme's own
 * live reference into `station.font_pack_face`, which is why `FontPackController.Uninstall` needs a
 * 409 guard and this route never does) — so this button only ever sees 204 or a genuine failure,
 * never a "still referenced" refusal to translate into a named message the way
 * `formatReferencedThemesMessage` does for the font tab.
 *
 * <b>204 → refresh, not local state surgery.</b> `wardrobe/page.tsx` is server-rendered
 * (`export const dynamic = "force-dynamic"`) — `router.refresh()` re-runs the server
 * `GET /api/avatar-packs` fetch and re-renders with the pack gone, the SAME pattern
 * `UninstallPackButton` uses.
 */
export function AvatarUninstallPackButton({ slug, displayName }: AvatarUninstallPackButtonProps): ReactNode {
  const router = useRouter();
  const confirm = useConfirm();
  const [busy, setBusy] = useState(false);

  async function handleUninstall(): Promise<void> {
    const confirmed = await confirm({
      title: "Uninstall avatar pack",
      consequence: `Uninstall "${displayName}"? Every one of its faces is removed from this station immediately.`,
      confirmLabel: "Uninstall",
      destructive: true,
    });
    if (!confirmed) return;

    setBusy(true);
    try {
      const resp = await fetch(`/api/avatar-packs/${encodeURIComponent(slug)}`, { method: "DELETE" });

      if (resp.status === 204) {
        toast.success(`"${displayName}" uninstalled.`);
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
      aria-label={`Uninstall ${displayName}`}
      disabled={busy}
      onClick={() => {
        void handleUninstall();
      }}
    >
      Uninstall
    </Button>
  );
}
