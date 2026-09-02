"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import {
  AD_SOURCE_LABELS,
  approveAdSpot,
  describeAdMutationFailure,
  retireAdSpot,
  retryAdSpot,
  type AdSpotDto,
} from "@/lib/ads-api";

interface AdSpotRowProps {
  spot: AdSpotDto;
  /** Re-fetch trigger — called after any successful verb (the Gardener `onChanged` convention:
   * re-fetch fresh via `router.refresh()`, never a local patch). */
  onChanged: () => void;
  /** Opens the shared editor pre-filled with this row. `AdsSection` passes this UNCONDITIONALLY
   * for every row, regardless of state — the prop itself carries no gating of its own. Whether it
   * is ever actually invoked is entirely this row's own {@link canEdit} check: the Edit button that
   * calls it only renders for `draft`/`failed`, so `onEdit` is reachable exactly when that button
   * is on screen and not otherwise (PLAN T404 review fold f — the docblock previously described
   * the PROP as conditionally passed, which it never is; the gating lives in the render, not the
   * prop). */
  onEdit: () => void;
}

/**
 * One ad spot row (SPEC F162.1; STORY-392 AC3/AC4; PLAN T404/T404b) — brand/length, the verbs legal
 * for its CURRENT state (mirrors `AdsController`'s own transition guards exactly, so no button here
 * ever fires a request the api would only 409), and — for a `ready` row — a real preview player.
 *
 * <b>The preview plays real bytes (PLAN T404b closes the T404 finding below).</b> `GET
 * /api/media/{id}/audio` (`MediaController.GetAudio`) now streams the persisted `library.media` row's
 * on-disk bytes with range support, so `<audio src="/api/media/{mediaId}/audio">` is a real player,
 * not a broken one — `next.config.ts`'s existing `/api/:path*` rewrite already carries it to the
 * backend, same as every other `/api/*` call this app makes; no rewrite-table change was needed.
 * `preload="none"` keeps a row that is merely rendered on screen (never opened) from ever issuing the
 * byte request — the player only fetches once an operator actually reveals it.
 *
 * (T404's own finding, for history: no endpoint served persisted media bytes at all at that point —
 * `GET /media/{id}` returns `MediaReference` metadata for Liquidsoap/the Orchestrator, and `GET
 * /api/media/{id}` is the same shape again behind auth. Wiring an `<audio>` tag at either would have
 * rendered a broken player that looked functional and silently failed, so T404 shipped an honest
 * notice instead and split the byte route out as T404b.)
 */
export function AdSpotRow({ spot, onChanged, onEdit }: AdSpotRowProps): ReactNode {
  const confirm = useConfirm();
  const [pending, setPending] = useState(false);
  const [previewOpen, setPreviewOpen] = useState(false);

  const canEdit = spot.state === "draft" || spot.state === "failed";
  const canApprove = spot.state === "draft";
  const canRetry = spot.state === "failed";
  const canRetire =
    spot.state === "ready" || spot.state === "draft" || spot.state === "approved" || spot.state === "failed";
  const canPreview = spot.state === "ready";

  async function run(action: () => Promise<void>): Promise<void> {
    setPending(true);
    try {
      await action();
    } finally {
      setPending(false);
    }
  }

  async function handleApprove(): Promise<void> {
    await run(async () => {
      const outcome = await approveAdSpot(spot.id, spot.version);
      if (!outcome.ok) {
        toast.error(describeAdMutationFailure(outcome));
        return;
      }
      toast.success("Spot approved.");
      onChanged();
    });
  }

  async function handleRetry(): Promise<void> {
    await run(async () => {
      const outcome = await retryAdSpot(spot.id, spot.version);
      if (!outcome.ok) {
        toast.error(describeAdMutationFailure(outcome));
        return;
      }
      toast.success("Spot retried.");
      onChanged();
    });
  }

  async function handleRetire(): Promise<void> {
    const confirmed = await confirm({
      title: "Retire this spot",
      consequence: "The spot moves to Retired and never airs again.",
      confirmLabel: "Retire",
      destructive: true,
    });
    if (!confirmed) return;

    await run(async () => {
      const outcome = await retireAdSpot(spot.id, spot.version);
      if (!outcome.ok) {
        toast.error(describeAdMutationFailure(outcome));
        return;
      }
      toast.success("Spot retired.");
      onChanged();
    });
  }

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 py-3">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <p className="truncate text-[0.9rem] text-ink">{spot.title}</p>
          <Chip>{AD_SOURCE_LABELS[spot.source]}</Chip>
        </div>
        <p className="truncate text-[0.8rem] text-mute">
          {spot.brand} · {spot.spotSeconds}s
        </p>
        {spot.state === "failed" && spot.failReason !== null && (
          <p className="mt-1 text-[0.78rem] text-danger">Failed: {spot.failReason}</p>
        )}
      </div>

      <div className="flex items-center gap-3">
        {canPreview && (
          <Button type="button" variant="secondary" onClick={() => setPreviewOpen((open) => !open)}>
            {previewOpen ? "Hide preview" : "Preview"}
          </Button>
        )}

        {canEdit && (
          <Button type="button" variant="secondary" disabled={pending} onClick={onEdit}>
            Edit
          </Button>
        )}

        {canApprove && (
          <Button type="button" disabled={pending} onClick={() => void handleApprove()}>
            Approve
          </Button>
        )}

        {canRetry && (
          <Button type="button" disabled={pending} onClick={() => void handleRetry()}>
            Retry
          </Button>
        )}

        {canRetire && (
          <Button type="button" variant="secondary" disabled={pending} onClick={() => void handleRetire()}>
            Retire
          </Button>
        )}
      </div>

      {previewOpen && canPreview && spot.mediaId !== null && (
        <audio className="w-full" controls preload="none" src={`/api/media/${spot.mediaId}/audio`} />
      )}
    </div>
  );
}
