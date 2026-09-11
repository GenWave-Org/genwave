"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { inFlightJob, listBackgroundMusic, updateAdSpot, type AdSpotDto, type BackgroundMusicOption } from "@/lib/ads-api";
import { FIELD_INPUT_CLASSES, FIELD_LABEL_CLASSES } from "../FieldRow";
import { JobRunner } from "./JobRunner";
import { StepActions } from "./StepActions";

interface HearStepProps {
  spot: AdSpotDto;
  onSpotUpdated: (spot: AdSpotDto) => void;
  onError: (detail: string) => void;
  onNext: () => void;
  onPreview: () => void;
  onCancelJob: () => void;
  onBack: () => void;
  onCancel: () => void;
}

/**
 * The Hear step (SPEC F174.4, F174.7; STORY-427 AC3/AC4; PLAN T442, T446, T448) — render a preview
 * (the wizard's poll effect carries the job to completion), listen to it in-dialog, and optionally
 * pick installed background music. `spot.bedMediaId` is the control's own source of truth (never a
 * separate local id). Once a title is committed (`spot.bedMediaId !== null`), "Let the station
 * pick" renders as a disabled option rather than a present-but-inert one (PLAN T448 ruling) — the
 * operator can still replace a committed title with a different one, but never clear it back to the
 * station's own pick, and the plain sentence under the select says so. No numeric input anywhere
 * (STORY-427 AC4) — every choice is a `<select>` option, never a typed id.
 */
export function HearStep({ spot, onSpotUpdated, onError, onNext, onPreview, onCancelJob, onBack, onCancel }: HearStepProps): ReactNode {
  const [musicOptions, setMusicOptions] = useState<BackgroundMusicOption[] | null>(null);
  const [pending, setPending] = useState(false);

  useEffect(() => {
    void (async () => {
      setMusicOptions(await listBackgroundMusic());
    })();
    // Runs once — the installed music list doesn't change over the wizard's own lifetime.
  }, []);

  const job = spot.job;
  const jobActive = inFlightJob(job) !== null;
  const bedCommitted = spot.bedMediaId !== null;

  async function handleBedChange(mediaId: number): Promise<void> {
    setPending(true);
    const outcome = await updateAdSpot(spot.id, spot.version, {
      sponsorId: null,
      title: null,
      brief: null,
      script: null,
      voicePlan: null,
      spotSeconds: null,
      bedMediaId: mediaId,
    });
    setPending(false);
    if (!outcome.ok) {
      onError(outcome.detail);
      return;
    }
    onSpotUpdated(outcome.spot);
  }

  return (
    <div className="flex flex-col gap-4">
      <JobRunner
        job={job}
        actionLabel="Render preview"
        progressLabel="Rendering…"
        onStart={onPreview}
        onCancel={onCancelJob}
        disabled={pending}
      />

      {spot.preview !== null && (
        <audio
          key={spot.preview.key}
          controls
          className="w-full"
          src={`/api/ads/${spot.id}/preview.wav?key=${encodeURIComponent(spot.preview.key)}`}
        />
      )}

      {spot.voicePlan !== null && spot.voicePlan.length > 0 && (
        <span role="group" aria-label="Voice cast" className="flex flex-wrap items-center gap-1">
          {spot.voicePlan.map((entry, index) => (
            <Chip key={`${index}-${entry.tag}`}>{`${entry.tag} · ${entry.voiceId}`}</Chip>
          ))}
        </span>
      )}

      <div className="flex flex-col gap-1.5">
        <label htmlFor="wizard-music" className={FIELD_LABEL_CLASSES}>
          Background music
        </label>
        <select
          id="wizard-music"
          aria-label="Background music"
          value={spot.bedMediaId ?? ""}
          onChange={(e) => {
            const raw = e.currentTarget.value;
            if (raw !== "") void handleBedChange(Number(raw));
          }}
          disabled={pending}
          className={FIELD_INPUT_CLASSES}
        >
          <option value="" disabled={bedCommitted}>
            Let the station pick
          </option>
          {musicOptions?.map((option) => (
            <option key={option.mediaId} value={option.mediaId}>
              {option.pack !== null ? `${option.title} — ${option.pack}` : option.title}
            </option>
          ))}
        </select>
        {bedCommitted && (
          <p className="text-[0.78rem] text-mute">
            Once you pick a title, the music can only be changed to another title.
          </p>
        )}
      </div>

      <StepActions onCancel={onCancel} onBack={onBack} disabled={jobActive || pending}>
        <Button type="button" disabled={jobActive || pending} onClick={onNext}>
          Next
        </Button>
      </StepActions>
    </div>
  );
}
