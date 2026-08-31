"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { formatDurationCell } from "@/lib/format-clock";
import {
  dismissGardenerFinding,
  evidenceChips,
  reenrichMedia,
  setEligibilityForMediaIds,
  type GardenerFindingDto,
  type GardenerKind,
} from "@/lib/gardener-api";
import { NeverPlayControl } from "../catalog/NeverPlayControl";

interface GardenerRowProps {
  kind: GardenerKind;
  finding: GardenerFindingDto;
  /** Re-fetch trigger — called after any successful verb (SPEC F153.10: re-fetch, never a local
   * patch — see GardenerView's own remarks). */
  onChanged: () => void;
  /** A near-duplicate group's own "Keep this one" button (DuplicateGroupCard's slot) — absent for
   * every other kind, and for a duplicate group's own row when rendered standalone would never
   * apply anyway. Rendered ahead of this row's own four verbs. */
  extraAction?: ReactNode;
}

/** The path's own file name — the row title's fallback when a track has no `title` yet (an
 * unenriched or tag-stripped row, SPEC F153.10's own "fallback to the path's file name"). */
function fileNameFromPath(path: string): string {
  const segments = path.split("/");
  return segments[segments.length - 1] ?? path;
}

/**
 * One finding row (SPEC F153.10, STORY-374 AC9): title/artist (falling back to the path's file
 * name), the path itself (monospace, truncated with a `title` attribute for the full value),
 * duration/plays/rating, the kind's own evidence as compact chips, and the four verbs every kind
 * shares — eligibility toggle, never-play toggle, re-enrich, dismiss. Shared by every section
 * (`GardenerSection`) AND by a near-duplicate group's own member list (`DuplicateGroupCard`, via
 * {@link GardenerRowProps.extraAction}) — ONE row implementation, not two.
 *
 * <b>T378 review BLOCK-1.</b> No icon-only verb may share an ambiguous glyph with a sibling on the
 * same row (the T369 regression this row originally repeated: eligibility and never-play both drew
 * the identical close/restore icon pair). Eligibility is now a real labelled checkbox — no reusable
 * eligibility TOGGLE component exists anywhere in this codebase to import (`CatalogTable.tsx`'s own
 * eligibility cell is read-only `Yes`/`No` text), so this mirrors
 * `catalog/[mediaId]/EditTrackForm.tsx`'s own checkbox+label markup for its "Eligible for playout"
 * field — the wrapping `<label>`'s own visible "Eligible" text IS the accessible name (LOW-C), never
 * a separate `aria-label`. Never-play REUSES `catalog/NeverPlayControl.tsx` verbatim — the exact production control
 * `RatingControls.tsx` renders the identical icon/label pair for on the Live page — rather than a
 * second hand-rolled copy; it stays icon-only, but it is now this row's ONLY icon-only control, so
 * it can never collide with a sibling verb again. Re-enrich and dismiss are visible-text secondary
 * `Button`s, matching `DuplicateGroupCard`'s own "Keep this one" button.
 *
 * Eligibility reuses `setEligibilityForMediaIds` with a single-id list — the SAME bulk endpoint
 * "Keep this one" calls with the group's other members' ids — rather than the ETag-guarded
 * `PATCH /api/media/{id}`: this page's own findings response carries no row version to build an
 * `If-Match` header from (SPEC F153.9's media projection has no `version`/`ETag` field), and the
 * bulk endpoint needs no optimistic-concurrency token at all.
 *
 * Dismiss confirms first (SMOKE-1, SPEC F153.2): a dismissed finding is never re-opened by any
 * pass, so it is the one verb on this row that is not a single click.
 */
export function GardenerRow({ kind, finding, onChanged, extraAction }: GardenerRowProps): ReactNode {
  const confirm = useConfirm();
  const [pending, setPending] = useState(false);
  const media = finding.media;
  const title = media.title ?? fileNameFromPath(media.path);

  async function run(action: () => Promise<void>): Promise<void> {
    setPending(true);
    try {
      await action();
    } finally {
      setPending(false);
    }
  }

  async function handleEligibilityToggle(): Promise<void> {
    await run(async () => {
      const outcome = await setEligibilityForMediaIds([finding.mediaId], !media.eligible);
      if (!outcome.ok) {
        toast.error(outcome.detail);
        return;
      }
      onChanged();
    });
  }

  async function handleReenrich(): Promise<void> {
    await run(async () => {
      const outcome = await reenrichMedia(finding.mediaId);
      if (!outcome.ok) {
        toast.error(outcome.detail);
        return;
      }
      toast.success("Re-analysis scheduled.");
      onChanged();
    });
  }

  async function handleDismiss(): Promise<void> {
    const confirmed = await confirm({
      title: "Dismiss finding",
      consequence: "The gardener will not raise this again for this track.",
      confirmLabel: "Dismiss",
    });
    if (!confirmed) return;

    await run(async () => {
      const outcome = await dismissGardenerFinding(finding.id);
      if (!outcome.ok) {
        toast.error(outcome.detail);
        return;
      }
      onChanged();
    });
  }

  const chips = evidenceChips(kind, finding.evidence);

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 py-3">
      <div className="min-w-0 flex-1">
        <p className="truncate text-[0.9rem] text-ink">{title}</p>
        {media.artist !== null && <p className="truncate text-[0.8rem] text-mute">{media.artist}</p>}
        <p className="truncate font-mono text-[0.72rem] text-mute" title={media.path}>
          {media.path}
        </p>
        {chips.length > 0 && (
          <div className="mt-1 flex flex-wrap gap-1.5">
            {chips.map((chip) => (
              <span
                key={chip}
                className="rounded-[999px] border border-line bg-surface-2 px-2 py-0.5 text-[0.7rem] text-mute"
              >
                {chip}
              </span>
            ))}
          </div>
        )}
      </div>

      <div className="flex items-center gap-4 text-[0.8rem] tabular-nums text-mute">
        <span>{formatDurationCell(media.durationMs)}</span>
        <span>{media.plays === 1 ? "1 play" : `${media.plays} plays`}</span>
        <span>{media.rating ?? "—"}</span>
      </div>

      <div className="flex items-center gap-3">
        {extraAction}

        {/* T378 review LOW-C: no `aria-label` override — the wrapping <label>'s own visible text
            ("Eligible") IS the accessible name, the ordinary label/input association every other
            checkbox+label pair in this codebase (EditTrackForm.tsx's "Eligible for playout") relies
            on, rather than a second, parallel "eligible"/"ineligible" string only screen readers see. */}
        <label className="flex min-h-10 items-center gap-1.5 text-[0.85rem] text-ink">
          <input
            type="checkbox"
            checked={media.eligible}
            disabled={pending}
            onChange={() => void handleEligibilityToggle()}
          />
          Eligible
        </label>

        <NeverPlayControl mediaId={String(finding.mediaId)} neverPlay={media.neverPlay} onChange={() => onChanged()} />

        <Button type="button" variant="secondary" disabled={pending} onClick={() => void handleReenrich()}>
          Re-enrich
        </Button>

        <Button type="button" variant="secondary" disabled={pending} onClick={() => void handleDismiss()}>
          Dismiss
        </Button>
      </div>
    </div>
  );
}
