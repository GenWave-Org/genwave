"use client";

import { useState, type ReactNode } from "react";
import { IconButton } from "@/components/ui/icon-button";
import { toast } from "@/components/ui/toast";
import { cn } from "@/lib/utils";
import { describeTasteThumbFailure, postTasteThumb, type TasteThumbDirection } from "@/lib/persona-taste-api";
import { TasteThumbDownIcon, TasteThumbUpIcon } from "./icons";

interface PersonaTasteThumbsProps {
  /** The stamped booth-log row id (SPEC F84.1, F84.6) — the wire's airing identity for BOTH the
   * now-playing and booth-log surfaces; the POST target is always this row's id, never a mediaId. */
  boothLogRowId: number;
  /** The persona STAMPED on this row at air time — never whichever persona happens to be active
   * now (F84.6/F84.7: "not the now-active one"). Callers resolve this once via
   * `usePersonaDirectory`/`personaNameOrFallback` and pass the resolved string down; this
   * component has no notion of "the active persona" at all, structurally, so it cannot drift onto
   * the wrong one. */
  personaName: string;
  className?: string;
}

/**
 * The persona-taste thumb control (SPEC F84.1, F84.5-F84.7; STORY-215) — deliberately a different
 * shape from the catalog's `RatingControls` (F33.11) so an operator can never confuse "curate the
 * library" with "teach the DJ" at a glance (F84.7's visual-distinctness requirement): real thumb
 * glyphs (not `RatingControls`' up/down chevrons), a brass (`--accent-2`) persona-attribution chip
 * in place of `RatingControls`' neutral numeric score pill, and exactly two directions — no third
 * never-play/restore button, since taste has no flag to clear.
 *
 * Idempotency affordance (F84.5): each direction disables itself independently the moment its own
 * POST resolves, whether the outcome was a fresh nudge or an already-recorded no-op — the two
 * outcomes settle to the exact same disabled state, so an operator can't tell (and doesn't need
 * to) which one happened. `settled` is local state scoped to one row/airing; callers are expected
 * to `key=` this component by `boothLogRowId` wherever the same slot renders a succession of
 * different rows (the now-playing card swapping in a new track) so a new row starts with both
 * directions live again, rather than inheriting the previous row's settled state.
 */
export function PersonaTasteThumbs({ boothLogRowId, personaName, className }: PersonaTasteThumbsProps): ReactNode {
  const [pending, setPending] = useState(false);
  const [settled, setSettled] = useState<ReadonlySet<TasteThumbDirection>>(new Set());

  async function handleThumb(direction: TasteThumbDirection): Promise<void> {
    setPending(true);
    const outcome = await postTasteThumb(boothLogRowId, direction);
    setPending(false);
    if (outcome.ok) {
      setSettled((prev) => new Set(prev).add(direction));
      return;
    }
    toast.error(describeTasteThumbFailure(outcome));
  }

  return (
    <div className={cn("flex items-center gap-1.5", className)}>
      <span className="inline-flex items-center rounded-[3px] border border-accent-2 px-1.5 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-accent-2">
        {personaName} taste
      </span>
      <IconButton
        label={`Taste up for ${personaName}`}
        disabled={pending || settled.has("up")}
        aria-pressed={settled.has("up")}
        className="border-accent-2 text-accent-2 hover:bg-surface-2"
        onClick={() => void handleThumb("up")}
      >
        <TasteThumbUpIcon />
      </IconButton>
      <IconButton
        label={`Taste down for ${personaName}`}
        disabled={pending || settled.has("down")}
        aria-pressed={settled.has("down")}
        className="border-accent-2 text-accent-2 hover:bg-surface-2"
        onClick={() => void handleThumb("down")}
      >
        <TasteThumbDownIcon />
      </IconButton>
    </div>
  );
}
