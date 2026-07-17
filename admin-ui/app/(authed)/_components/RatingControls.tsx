"use client";

import { useState, type ReactNode } from "react";
import { IconButton } from "@/components/ui/icon-button";
import { toast } from "@/components/ui/toast";
import { cn } from "@/lib/utils";
import {
  describeRatingFailure,
  setNeverPlay,
  voteTrack,
  type RatingEntry,
  type VoteDirection,
} from "@/lib/broadcast-api";
import { CloseIcon, RestoreIcon, VoteDownIcon, VoteUpIcon } from "./icons";

export type RatingControlsValue = Pick<RatingEntry, "score" | "neverPlay">;

interface RatingControlsProps {
  mediaId: string;
  value: RatingControlsValue;
  /**
   * Called with the fresh state after a successful vote or never-play toggle — the response
   * body IS the new state (F33.11: "no refetch"). Callers fold this back into whatever they hold
   * for this mediaId.
   */
  onChange: (next: RatingControlsValue) => void;
  className?: string;
}

/**
 * Score chip + vote-up/vote-down/never-play controls (SPEC F33.11, STORY-114) — the one
 * rating widget shared by the Live page's play-history rows and now-playing card, the only two
 * votable surfaces this epic ships (S6). Owns the full interaction: a click calls the S4
 * endpoint directly — deliberately NOT through `useRowPatch` (rating writes are ETag-free by
 * design, PLAN.md Epic S sequencing note) — disables all three controls while any one request is
 * in flight, and on success folds the response body back via `onChange` with no refetch; on
 * failure it toasts a classified, human message (401/403/404/network get distinct copy) and
 * leaves the caller's `value` untouched.
 *
 * Chip is a quiet pill (tabular numerals, per design-aesthetic); all three controls reuse the
 * shared `IconButton` (40px min touch target, visible focus ring already built in, hover/focus
 * tooltip with the same copy as its aria-label — SPEC F62.1–F62.2). The never-play button's icon
 * reflects current state — X when playable, a restore arrow when flagged — so the X is never a
 * one-way door (F23.2 reachability posture).
 */
export function RatingControls({ mediaId, value, onChange, className }: RatingControlsProps): ReactNode {
  const [pending, setPending] = useState(false);

  async function handleVote(direction: VoteDirection): Promise<void> {
    setPending(true);
    const outcome = await voteTrack(mediaId, direction);
    setPending(false);
    if (outcome.ok) {
      onChange({ score: outcome.score, neverPlay: value.neverPlay });
      return;
    }
    toast.error(describeRatingFailure(outcome.kind, outcome.status));
  }

  async function handleNeverPlay(): Promise<void> {
    setPending(true);
    const outcome = await setNeverPlay(mediaId, !value.neverPlay);
    setPending(false);
    if (outcome.ok) {
      onChange({ score: value.score, neverPlay: outcome.neverPlay });
      return;
    }
    toast.error(describeRatingFailure(outcome.kind, outcome.status));
  }

  return (
    <div className={cn("flex items-center gap-1.5", className)}>
      <span
        aria-label={`Score ${value.score}`}
        className="inline-flex min-w-10 items-center justify-center rounded-[999px] border border-line bg-surface-2 px-2 py-1 text-[0.75rem] font-semibold tabular-nums text-ink"
      >
        {value.score}
      </span>
      <IconButton label="Vote up" disabled={pending} onClick={() => void handleVote("up")}>
        <VoteUpIcon />
      </IconButton>
      <IconButton label="Vote down" disabled={pending} onClick={() => void handleVote("down")}>
        <VoteDownIcon />
      </IconButton>
      <IconButton
        label={value.neverPlay ? "Restore to rotation" : "Never play"}
        disabled={pending}
        onClick={() => void handleNeverPlay()}
      >
        {value.neverPlay ? <RestoreIcon /> : <CloseIcon />}
      </IconButton>
    </div>
  );
}
