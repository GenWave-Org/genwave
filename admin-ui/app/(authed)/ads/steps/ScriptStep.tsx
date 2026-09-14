"use client";

import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { inFlightJob, updateAdSpot, type AdSpotDto } from "@/lib/ads-api";
import { parseScriptTags } from "@/lib/ad-script-tags";
import { FieldRow, FIELD_INPUT_CLASSES } from "../FieldRow";
import { JobRunner } from "./JobRunner";
import { StepActions } from "./StepActions";

interface ScriptStepProps {
  spot: AdSpotDto;
  onSpotUpdated: (spot: AdSpotDto) => void;
  onError: (detail: string) => void;
  onNext: () => void;
  onWrite: () => void;
  onCancelJob: () => void;
  onBack: () => void;
  onCancel: () => void;
}

/**
 * The Script step (SPEC F160.4, F174.3, F174.6; PLAN T441, T448) — "Write it for me" enqueues the
 * station's own write job (the wizard's own poll effect carries it to completion; this step only
 * renders whatever `spot.job` currently says), or the operator types the script directly. Either
 * way, an unlabeled line still reaches the announcer voice — F174.6's own rule — so the purpose
 * sentence above this step says so rather than leaving that a surprise once it airs.
 */
export function ScriptStep({ spot, onSpotUpdated, onError, onNext, onWrite, onCancelJob, onBack, onCancel }: ScriptStepProps): ReactNode {
  const [script, setScript] = useState(spot.script ?? "");
  const [pending, setPending] = useState(false);

  const job = spot.job;
  const jobActive = inFlightJob(job) !== null;
  const wasJobActiveRef = useRef(jobActive);

  // A write job that just finished replaces the textarea with the station's own copy; a job that
  // never ran (the operator is simply typing) never touches it — `jobActive` only flips false→true
  // →false around a real write, never on an ordinary keystroke.
  useEffect(() => {
    if (wasJobActiveRef.current && !jobActive && spot.script !== null) setScript(spot.script);
    wasJobActiveRef.current = jobActive;
  }, [jobActive, spot.script]);

  const tags = useMemo(() => parseScriptTags(script), [script]);

  async function handleNext(): Promise<void> {
    if (script.trim() === "") {
      onError("Write a script, or let the station write one, before continuing.");
      return;
    }
    if (script === spot.script) {
      onNext();
      return;
    }

    setPending(true);
    const outcome = await updateAdSpot(spot.id, spot.version, {
      sponsorId: null,
      title: null,
      brief: null,
      script,
      voicePlan: null,
      spotSeconds: null,
      bedMediaId: null,
    });
    setPending(false);
    if (!outcome.ok) {
      onError(outcome.detail);
      return;
    }
    onSpotUpdated(outcome.spot);
    onNext();
  }

  return (
    <div className="flex flex-col gap-4">
      <JobRunner
        job={job}
        kind="write"
        actionLabel="Write it for me"
        progressLabel="Writing…"
        onStart={onWrite}
        onCancel={onCancelJob}
        disabled={pending}
      />

      <FieldRow label="Script" htmlFor="wizard-script">
        <textarea
          id="wizard-script"
          rows={6}
          value={script}
          onChange={(e) => setScript(e.currentTarget.value)}
          disabled={jobActive || pending}
          placeholder={"ANNOUNCER: ..."}
          className={`${FIELD_INPUT_CLASSES} resize-y py-2 font-mono`}
        />
      </FieldRow>

      {tags.length > 0 && (
        <span role="group" aria-label="Voice cast" className="flex flex-wrap items-center gap-1">
          {tags.map((tag) => (
            <Chip key={tag}>{tag}</Chip>
          ))}
        </span>
      )}

      <StepActions onCancel={onCancel} onBack={onBack} disabled={jobActive || pending}>
        <Button type="button" disabled={jobActive || pending} onClick={() => void handleNext()}>
          Next
        </Button>
      </StepActions>
    </div>
  );
}
