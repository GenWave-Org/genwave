"use client";

import type { ReactNode } from "react";
import { useRouter } from "next/navigation";
import { PurgeUnavailableAction as SharedPurgeUnavailableAction } from "../_components/PurgeUnavailableAction";

/**
 * gh-#113 — "Purge hidden tracks…" for the catalog's revealed-unavailable view. A thin,
 * catalog-specific wrapper over the shared `_components/PurgeUnavailableAction` (T378 review
 * MED-2: the dry-run/confirm/destructive-call wire contract now lives in exactly one place); kept
 * as a named export from THIS path, unchanged, so existing call sites and
 * `catalog-purge-unavailable.spec.tsx` need no edit at all. Passes `router.refresh()` as the
 * post-purge refresh — this page's own rows are server-rendered, unlike the Gardener page's.
 */
export function PurgeUnavailableAction(): ReactNode {
  const router = useRouter();

  return (
    <SharedPurgeUnavailableAction
      title="Purge hidden tracks"
      triggerLabel="Purge hidden tracks…"
      onPurged={() => router.refresh()}
    />
  );
}
