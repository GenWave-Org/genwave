"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { approveAdSpot, describeApproved, fetchAdSpot, type AdSpotDto } from "@/lib/ads-api";
import { StepActions } from "./StepActions";

const STALE_PREVIEW_HINT = "The preview is out of date. Render it again to approve what you heard.";

interface ApproveStepProps {
  spot: AdSpotDto;
  onApproved: (spot: AdSpotDto) => void;
  onSpotUpdated: (spot: AdSpotDto) => void;
  onError: (detail: string) => void;
  onBack: () => void;
  onCancel: () => void;
}

/**
 * The Approve step (SPEC F159.4, F174.4; PLAN T445, T448) — "Approve as heard" only fires once a
 * fresh, non-stale preview exists (the sentence below explains why otherwise). "Approve without a
 * preview" renders ONLY when no preview has ever been rendered (`spot.preview === null`) — the
 * server 409s `preview_stale` whenever ANY preview exists and is stale, so the button would be a
 * dead end the moment one does; once a preview exists at all, listening to it (or rendering a
 * fresh one) is the only path to approval (PLAN T448 ruling). A 409 `preview_stale` (a race — a
 * background music change landed between this row's last read and the click) re-fetches the row
 * so the dialog reflects the CURRENT staleness, surfacing the server's own `detail` for that
 * specific 409 rather than a second, client-side wording of the same rule.
 *
 * On success, both buttons toast `describeApproved` (STORY-433 AC4–AC7; PLAN T458) naming the
 * render window before calling `onApproved` — the wizard-level `handleApproved` in `SpotWizard.tsx`
 * stays copy-free.
 */
export function ApproveStep({ spot, onApproved, onSpotUpdated, onError, onBack, onCancel }: ApproveStepProps): ReactNode {
  const [pending, setPending] = useState(false);

  const stale = spot.preview !== null && spot.preview.stale;
  const canApproveAsHeard = spot.preview !== null && !spot.preview.stale;

  async function handleApprove(): Promise<void> {
    setPending(true);
    const outcome = await approveAdSpot(spot.id, spot.version);
    if (outcome.ok) {
      setPending(false);
      toast.success(describeApproved(outcome.spot));
      onApproved(outcome.spot);
      return;
    }

    if (outcome.type === "preview_stale") {
      const refreshed = await fetchAdSpot(spot.id);
      setPending(false);
      if (refreshed.ok) onSpotUpdated(refreshed.spot);
      onError(outcome.detail);
      return;
    }

    setPending(false);
    onError(outcome.detail);
  }

  return (
    <div className="flex flex-col gap-4">
      {stale && <p className="text-[0.82rem] text-mute">{STALE_PREVIEW_HINT}</p>}

      <StepActions onCancel={onCancel} onBack={onBack} disabled={pending}>
        {spot.preview === null && (
          <Button type="button" variant="secondary" disabled={pending} onClick={() => void handleApprove()}>
            Approve without a preview
          </Button>
        )}
        <Button type="button" disabled={pending || !canApproveAsHeard} onClick={() => void handleApprove()}>
          Approve as heard
        </Button>
      </StepActions>
    </div>
  );
}
