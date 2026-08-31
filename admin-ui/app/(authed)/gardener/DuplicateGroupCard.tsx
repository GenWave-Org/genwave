"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { setEligibilityForMediaIds, type GardenerDuplicateGroupDto, type GardenerFindingDto } from "@/lib/gardener-api";
import { GardenerRow } from "./GardenerRow";

interface DuplicateGroupCardProps {
  group: GardenerDuplicateGroupDto;
  onChanged: () => void;
}

/**
 * One near-duplicate group (SPEC F153.10, STORY-376 AC6, PLAN T378): every member renders through
 * the SAME {@link GardenerRow} every other kind uses (the four shared verbs still apply per member —
 * STORY-374 AC9 draws no exception for near_duplicate), plus a "Keep this one" button per member —
 * clicking it on row A confirms "Mark N siblings ineligible?" and then calls
 * `setEligibilityForMediaIds` with every OTHER member's `mediaId`, `eligible: false` — ONE bulk call,
 * never N per-row PATCHes. Confirmed, THEN refetches via the same `onChanged` every other verb here
 * uses (ORCHESTRATOR ruling 2).
 */
export function DuplicateGroupCard({ group, onChanged }: DuplicateGroupCardProps): ReactNode {
  const confirm = useConfirm();
  const [pendingKeepId, setPendingKeepId] = useState<number | null>(null);

  async function handleKeepThisOne(keep: GardenerFindingDto): Promise<void> {
    const others = group.members.filter((member) => member.id !== keep.id);
    if (others.length === 0) return;

    const confirmed = await confirm({
      title: "Keep this one",
      consequence: `Mark ${others.length} sibling${others.length === 1 ? "" : "s"} ineligible?`,
      confirmLabel: "Keep this one",
    });
    if (!confirmed) return;

    setPendingKeepId(keep.id);
    try {
      const outcome = await setEligibilityForMediaIds(
        others.map((member) => member.mediaId),
        false
      );
      if (!outcome.ok) {
        toast.error(outcome.detail);
        return;
      }
      toast.success(`Marked ${outcome.affected} sibling${outcome.affected === 1 ? "" : "s"} ineligible.`);
      onChanged();
    } finally {
      setPendingKeepId(null);
    }
  }

  return (
    <div className="rounded-[6px] border border-line bg-surface-2 p-3">
      <p className="text-[0.72rem] font-semibold uppercase tracking-[0.1em] text-mute">
        Group {group.groupKey ?? "—"}
      </p>
      <div className="mt-2 divide-y divide-line">
        {group.members.map((finding) => (
          <GardenerRow
            key={finding.id}
            kind="near_duplicate"
            finding={finding}
            onChanged={onChanged}
            extraAction={
              <Button
                type="button"
                variant="secondary"
                disabled={pendingKeepId !== null}
                onClick={() => void handleKeepThisOne(finding)}
              >
                Keep this one
              </Button>
            }
          />
        ))}
      </div>
    </div>
  );
}
