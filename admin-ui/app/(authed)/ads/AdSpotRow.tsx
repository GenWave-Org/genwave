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
 * One ad spot row (SPEC F162.1; STORY-392 AC3/AC4; PLAN T404) — brand/length, the verbs legal for
 * its CURRENT state (mirrors `AdsController`'s own transition guards exactly, so no button here
 * ever fires a request the api would only 409), and — for a `ready` row — the preview affordance.
 *
 * <b>The preview affordance is honest, not a real player (PLAN T404's own finding — see the class
 * remarks below).</b> No endpoint in this codebase streams a persisted `library.media` row's bytes
 * to the browser: `GET /media/{id}` (`MediaEndpoints`, the anonymous hot path Liquidsoap/the
 * Orchestrator use) returns a `MediaReference` — title/loudness/duration METADATA, never a byte
 * stream — and `GET /api/media/{id}` (`MediaController`) is the same shape again, behind auth.
 * `PersonaPreview`'s own `<audio>` tag (the only in-browser playback anywhere in this admin UI)
 * plays an EPHEMERAL render from `POST /api/tts/preview`, not a persisted row. Wiring
 * `<audio src="/media/{id}">` here would render a broken player (that path 404s through this app's
 * own `next.config.ts` rewrite table besides — only `/api/*` and `/fonts/*` are proxied) that LOOKS
 * functional and silently fails; that is worse than admitting the gap. STORY-392 AC4 ("the rendered
 * artifact plays in the browser") is therefore NOT fully met by this task — closing it for real
 * needs a backend byte-serving route (and the matching `next.config.ts` rewrite), which is outside
 * this task's owned surface (the admin-ui TSX half only). Filed here rather than silently shipped:
 * the honest affordance below states exactly that, using only fields this row already carries
 * (`mediaId`, `spotSeconds`) — no fetch, nothing that could itself fail.
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

      {previewOpen && canPreview && (
        <p className="w-full text-[0.8rem] text-mute">
          Rendered to media #{spot.mediaId ?? "?"} · {spot.spotSeconds}s — playback isn&apos;t wired into
          this console yet; the spot airs on schedule.
        </p>
      )}
    </div>
  );
}
