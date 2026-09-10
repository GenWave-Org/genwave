"use client";

import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { inFlightJob, type AdSpotJobDto } from "@/lib/ads-api";

export interface JobRunnerProps {
  job: AdSpotJobDto | null;
  actionLabel: string;
  progressLabel: string;
  onStart: () => void;
  onCancel: () => void;
  /** An unrelated save-in-flight the caller also wants to block starting a new job during (e.g.
   * `ScriptStep`'s own "Next" PATCH) — never affects Cancel, which stays available whenever a job
   * is genuinely running regardless of this flag. */
  disabled?: boolean;
}

/**
 * The Script/Hear steps' shared job control (SPEC F174.3, F174.4; PLAN T441, T442, T448) — one
 * start/cancel button pair plus progress/error copy, reused verbatim rather than the two steps each
 * carrying a near-identical block. Once `job` is no longer in flight ({@link inFlightJob}), the
 * action button re-enables, no Cancel renders (there is nothing left to cancel), and no progress
 * copy shows — only the error, which renders whenever it is present, in flight or not.
 */
export function JobRunner({ job, actionLabel, progressLabel, onStart, onCancel, disabled = false }: JobRunnerProps): ReactNode {
  const activeJob = inFlightJob(job);

  return (
    <>
      <div className="flex items-center gap-2">
        <Button type="button" variant="secondary" disabled={activeJob !== null || disabled} onClick={onStart}>
          {actionLabel}
        </Button>
        {activeJob !== null && (
          <Button type="button" variant="secondary" onClick={onCancel}>
            Cancel
          </Button>
        )}
      </div>

      {activeJob !== null && (
        <p className="text-[0.82rem] text-mute">
          {activeJob.waitingForStation ? "Waiting for the station to finish talking…" : progressLabel}
        </p>
      )}
      {job !== null && job.error !== null && (
        <p role="alert" aria-live="assertive" className="text-[0.82rem] text-danger">
          {job.error}
        </p>
      )}
    </>
  );
}
