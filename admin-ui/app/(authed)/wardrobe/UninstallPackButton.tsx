"use client";

import { useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { readErrorMessage } from "@/lib/problem-details";
import { formatReferencedThemesMessage } from "./referenced-themes";

export interface UninstallPackButtonProps {
  /** The installed pack's slug — `DELETE /api/fonts/{slug}`'s route target (SPEC F104.14). */
  slug: string;
  /** The pack's display family — used in the confirm dialog and toast copy only, never in the
   * request itself (the route is keyed on `slug`, mirroring `FontInstallModal`'s own split). */
  family: string;
}

/**
 * The Wardrobe's per-pack uninstall affordance (gh-#428, SPEC F104.14) — the read-only listing's one
 * exception: everything else on this page (`WardrobeClient`) renders `GET /api/fonts` verbatim and
 * issues no requests of its own; this button is the one place the page now writes. Mirrors
 * `FontInstallModal`'s confirm/fetch/error idioms (same-origin `fetch`, `readErrorMessage` for the
 * ProblemDetails `detail`) but trades that dialog's own inline error state for the
 * `useConfirm()`/toast shape `PurgeUnavailableAction`/`LibrariesTab` already use for a page that
 * doesn't otherwise track local mutation state: the confirm dialog just asks yes/no, and the
 * outcome — success or failure — surfaces as a toast once the request settles.
 *
 * <b>204 → refresh, not local state surgery.</b> This page is server-rendered
 * (`export const dynamic = "force-dynamic"`, `page.tsx`) — `router.refresh()` re-runs the server
 * `GET /api/fonts` fetch and re-renders with the pack gone, the same pattern
 * `PurgeUnavailableAction` uses, rather than this component reaching into `WardrobeClient`'s own
 * `packs` prop to remove a row by hand.
 *
 * <b>409 — named, not generic (SPEC F104.14's own "naming every referencing theme" contract).</b>
 * The server's `detail` prose already names every blocking theme; `formatReferencedThemesMessage`
 * reshapes it into "In use by: &lt;themes&gt;" for the toast rather than relaying the full sentence
 * verbatim, and degrades to that same sentence unchanged on the one shape it can't parse (see that
 * function's own remarks).
 */
export function UninstallPackButton({ slug, family }: UninstallPackButtonProps): ReactNode {
  const router = useRouter();
  const confirm = useConfirm();
  const [busy, setBusy] = useState(false);

  async function handleUninstall(): Promise<void> {
    const confirmed = await confirm({
      title: "Uninstall font pack",
      consequence: `Uninstall "${family}"? Every one of its faces is removed from this station immediately.`,
      confirmLabel: "Uninstall",
      destructive: true,
    });
    if (!confirmed) return;

    setBusy(true);
    try {
      const resp = await fetch(`/api/fonts/${encodeURIComponent(slug)}`, { method: "DELETE" });

      if (resp.status === 204) {
        toast.success(`"${family}" uninstalled.`);
        router.refresh();
        return;
      }

      const detail = await readErrorMessage(resp);
      toast.error(resp.status === 409 ? formatReferencedThemesMessage(detail) : detail);
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
      aria-label={`Uninstall ${family}`}
      disabled={busy}
      onClick={() => {
        void handleUninstall();
      }}
    >
      Uninstall
    </Button>
  );
}
