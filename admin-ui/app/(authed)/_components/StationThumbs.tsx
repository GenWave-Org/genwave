"use client";

import { useState, type ReactNode } from "react";
import { IconButton } from "@/components/ui/icon-button";
import { toast } from "@/components/ui/toast";
import { cn } from "@/lib/utils";
import {
  describeStationThumbFailure,
  postStationThumb,
  type StationThumbDirection,
  type StationThumbResult,
} from "@/lib/station-thumb-api";
import { Icon } from "./Icon";

interface StationThumbsProps {
  /** The stamped booth-log row id (SPEC F150.1, F150.8) — the same wire identity
   * `PersonaTasteThumbs` posts against for the same row, but this control never shares a click,
   * a write path, or a piece of state with it: the POST target is
   * `/api/booth-log/{id}/station-thumb`, reaching only `IThumbStore` server-side, never the
   * persona-taste accrual ledger (`BoothLogController.ThumbStation`'s own disjointness remarks). */
  boothLogRowId: number;
  className?: string;
}

/** Result-token copy (SPEC F150.1, F150.8; Dean's capitals rule) — a `Record<StationThumbResult,
 * string>` rather than a `switch`/fallback so a new token added to the wire's closed set fails
 * `tsc` here instead of silently falling through to generic copy. */
const RESULT_COPY: Record<StationThumbResult, string> = {
  recorded: "Recorded",
  unchanged: "Already recorded",
  flipped: "Flipped",
  ignored: "Ignored — station imaging",
};

/**
 * The station-level rotation-thumb control (SPEC F150.1, F150.8; STORY-370) — the sibling of
 * `PersonaTasteThumbs` that sits BESIDE it wherever this UI chooses to render both (currently:
 * every row `PersonaTasteThumbs` is also offered on — see `StationThumbs`' own call sites for why;
 * SPEC F150.8 itself only requires "Live now-playing and booth-log track rows", not that
 * co-occurrence), made deliberately un-confusable with it (F150.1's "this is the STATION's
 * rotation signal, not this DJ's taste" legibility requirement, mirroring F84.7's own
 * visual-distinctness posture for the taste-thumb-vs-catalog-vote pairing): its own accessible
 * names ("Station thumbs up"/"Station thumbs down" — a screen reader hears the difference from
 * "Taste up for {persona}" without any extra context), a neutral "Station" label chip in place of
 * the taste pair's brass persona-attribution chip, and its own dedicated `station-thumb-up`/
 * `station-thumb-down` glyphs (`icons.tsx`) — a tuning-dial needle, distinct from BOTH the taste
 * pair's hand-shaped glyph it sits directly next to AND `RatingControls`' catalog-vote chevron
 * (T369 review HIGH-1: reusing `vote-up`/`vote-down` put two different ledgers 8px apart on the
 * now-playing card, told apart only by tooltip — not good enough).
 *
 * Idempotency affordance mirrors `PersonaTasteThumbs`, with two structural differences. First:
 * taste tracks each direction independently (two artist-rule nudges can both have fired
 * historically), but a station thumb is a single current value — a `"flipped"` result means the
 * OTHER direction was previously recorded and this click just replaced it — so `settled` here is
 * one direction (or none), not an independent per-direction set; the previously-settled direction
 * re-enables the moment the other one is tapped, so an operator can flip back. Second: an
 * `"ignored"` result (safe-scope/unknown media — SPEC F150.1) wrote nothing server-side, so it
 * settles NEITHER button — both stay live and unpressed, only the toast explains why nothing
 * moved (T369 review MED-2).
 */
export function StationThumbs({ boothLogRowId, className }: StationThumbsProps): ReactNode {
  const [pending, setPending] = useState(false);
  const [settled, setSettled] = useState<StationThumbDirection | null>(null);

  async function handleThumb(direction: StationThumbDirection): Promise<void> {
    setPending(true);
    const outcome = await postStationThumb(boothLogRowId, direction);
    setPending(false);
    if (outcome.ok) {
      if (outcome.result !== "ignored") {
        setSettled(direction);
      }
      toast.success(RESULT_COPY[outcome.result]);
      return;
    }
    toast.error(describeStationThumbFailure(outcome));
  }

  return (
    <div className={cn("flex items-center gap-1.5", className)}>
      <span className="inline-flex items-center rounded-[3px] border border-line px-1.5 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-mute">
        Station
      </span>
      <IconButton
        label="Station thumbs up"
        disabled={pending || settled === "up"}
        aria-pressed={settled === "up"}
        onClick={() => void handleThumb("up")}
      >
        <Icon name="station-thumb-up" />
      </IconButton>
      <IconButton
        label="Station thumbs down"
        disabled={pending || settled === "down"}
        aria-pressed={settled === "down"}
        onClick={() => void handleThumb("down")}
      >
        <Icon name="station-thumb-down" />
      </IconButton>
    </div>
  );
}
