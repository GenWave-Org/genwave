"use client";

import { useState, type ReactNode } from "react";
import { IconButton } from "@/components/ui/icon-button";
import { toast } from "@/components/ui/toast";
import { describeRatingFailure, setNeverPlay } from "@/lib/broadcast-api";
import { CloseIcon, RestoreIcon } from "../_components/icons";

export interface NeverPlayControlProps {
  mediaId: string;
  neverPlay: boolean;
  /** Called with the PUT response's fresh flag on success (F33.11 "no refetch" posture) — the
   * catalog table folds this into the row's local state and refreshes so a `?never-play=true`
   * filtered view drops a just-restored row (SPEC F33.10, F33.12). */
  onChange: (neverPlay: boolean) => void;
}

/**
 * Restore/never-play toggle for one catalog row (SPEC F33.12, STORY-115). Symmetric with the
 * Live page's control (STORY-114, `RatingControls`): X flags a playable row, the restore icon
 * un-flags it — the X is never a one-way door (F23.2 reachability posture), and this symmetry
 * costs nothing extra to build since the icons and the S4 PUT are already shared.
 *
 * Calls the shared `setNeverPlay` (S4's `PUT /api/media/{id}/never-play`) directly — deliberately
 * NOT through `useRowPatch` (rating writes are ETag-free by design, PLAN.md Epic S sequencing
 * note). On success folds the response's `neverPlay` back via `onChange`; on failure toasts a
 * classified message and leaves the row's badge/icon untouched. Renders through the shared
 * `IconButton` so the hover/focus tooltip always carries the same copy as the aria-label
 * (SPEC F62.1–F62.2).
 */
export function NeverPlayControl({ mediaId, neverPlay, onChange }: NeverPlayControlProps): ReactNode {
  const [pending, setPending] = useState(false);

  async function handleClick(): Promise<void> {
    setPending(true);
    const outcome = await setNeverPlay(mediaId, !neverPlay);
    setPending(false);
    if (outcome.ok) {
      onChange(outcome.neverPlay);
      return;
    }
    toast.error(describeRatingFailure(outcome.kind, outcome.status));
  }

  return (
    <IconButton
      label={neverPlay ? "Restore to rotation" : "Never play"}
      disabled={pending}
      onClick={() => void handleClick()}
    >
      {neverPlay ? <RestoreIcon /> : <CloseIcon />}
    </IconButton>
  );
}
