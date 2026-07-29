"use client";

import { useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { purgeUnavailable } from "@/lib/purge-unavailable-api";

/** The endpoint's own default window — named here only for the dialog/toast copy. */
const OLDER_THAN_DAYS = 7;

/**
 * gh-#113 — "Purge hidden tracks…" for the catalog's revealed-unavailable view. Two-phase per the
 * design-aesthetic destructive-action treatment: a dryRun fetch first so the confirm dialog NAMES
 * the count in plain words, then the destructive call only on confirm (solid --danger lives inside
 * the dialog; this trigger stays a quiet secondary button). The server's 409 tripwire — more than
 * half the library would go, the mount-outage pattern — surfaces as its own explanation, never a
 * generic failure.
 */
export function PurgeUnavailableAction(): ReactNode {
  const router = useRouter();
  const confirm = useConfirm();
  const [busy, setBusy] = useState(false);

  async function runPurge(): Promise<void> {
    setBusy(true);
    try {
      const counted = await purgeUnavailable({ olderThanDays: OLDER_THAN_DAYS, dryRun: true });

      if (counted.kind === "refused" || counted.kind === "error") {
        toast.error(counted.message);
        return;
      }
      if (counted.kind !== "counted") {
        return;
      }

      if (counted.wouldDelete === 0) {
        toast.success(
          `Nothing to purge — no tracks have been unavailable for more than ${OLDER_THAN_DAYS} days.`
        );
        return;
      }

      const plural = counted.wouldDelete === 1 ? "track" : "tracks";
      const confirmed = await confirm({
        title: "Purge hidden tracks",
        consequence:
          `This permanently deletes ${counted.wouldDelete} ${plural} that ` +
          `${counted.wouldDelete === 1 ? "has" : "have"} been unavailable for more than ` +
          `${OLDER_THAN_DAYS} days, along with ${counted.wouldDelete === 1 ? "its" : "their"} ` +
          "ratings. This cannot be undone.",
        confirmLabel: `Purge ${counted.wouldDelete} ${plural}`,
        destructive: true,
      });
      if (!confirmed) {
        return;
      }

      const purged = await purgeUnavailable({ olderThanDays: OLDER_THAN_DAYS, dryRun: false });
      if (purged.kind === "purged") {
        toast.success(`Purged ${purged.deleted} ${purged.deleted === 1 ? "track" : "tracks"}.`);
        router.refresh();
      } else if (purged.kind === "refused" || purged.kind === "error") {
        toast.error(purged.message);
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <Button variant="secondary" disabled={busy} onClick={() => void runPurge()}>
      Purge hidden tracks…
    </Button>
  );
}
