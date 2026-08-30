"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { purgeUnavailable } from "@/lib/purge-unavailable-api";

/** The endpoint's own default window — named here only for the dialog/toast copy. */
const OLDER_THAN_DAYS = 7;

export interface PurgeUnavailableActionProps {
  /** The confirm dialog's own static title (e.g. "Purge hidden tracks", "Purge unavailable") —
   * distinct from the dialog's dynamic `confirmLabel`, which always names the live count. */
  title: string;
  /** The trigger button's own visible text (e.g. "Purge hidden tracks…", "Purge unavailable…"). */
  triggerLabel: string;
  /** Called after a successful (non-zero) purge. A server-rendered caller (Catalog's revealed-
   * unavailable view) passes `() => router.refresh()`; a fully client-fetched caller (the Gardener
   * page, which has no server props to invalidate) passes its own re-fetch. */
  onPurged: () => void;
}

/**
 * gh-#113's own "Purge…" trigger, two-phase per the design-aesthetic destructive-action treatment:
 * a dryRun fetch first so the confirm dialog NAMES the count in plain words, then the destructive
 * call only on confirm (solid --danger lives inside the dialog; this trigger stays a quiet
 * secondary button). The server's 409 tripwire — more than half the library would go, the
 * mount-outage pattern — surfaces as its own explanation, never a generic failure.
 *
 * T378 review MED-2: lifted out of `catalog/PurgeUnavailableAction.tsx` into this shared home once
 * the Gardener page's dead_file section needed the IDENTICAL dry-run/confirm/destructive-call wire
 * contract with its own copy and its own post-purge refresh — the copy (`title`/`triggerLabel`) and
 * the refresh strategy (`onPurged`) are the only two things that ever varied between the two call
 * sites; everything else (the count, the pluralisation, the 409 relay) is this ONE implementation
 * now, not two.
 */
export function PurgeUnavailableAction({ title, triggerLabel, onPurged }: PurgeUnavailableActionProps): ReactNode {
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
        title,
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
        onPurged();
      } else if (purged.kind === "refused" || purged.kind === "error") {
        toast.error(purged.message);
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <Button type="button" variant="secondary" disabled={busy} onClick={() => void runPurge()}>
      {triggerLabel}
    </Button>
  );
}
