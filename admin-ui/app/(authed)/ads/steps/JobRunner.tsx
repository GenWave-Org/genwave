"use client";

import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { inFlightJob, type AdJobKind, type AdSpotJobDto } from "@/lib/ads-api";

export interface JobRunnerProps {
  job: AdSpotJobDto | null;
  /** The job kind this control starts and therefore owns the error for — `ScriptStep` passes
   * `"write"`, `HearStep` passes `"preview"`. Routes `job.error` to whichever step's own job
   * actually failed (STORY-435 AC6–AC8) instead of both steps echoing it. */
  kind: AdJobKind;
  actionLabel: string;
  progressLabel: string;
  onStart: () => void;
  onCancel: () => void;
  /** An unrelated save-in-flight the caller also wants to block starting a new job during (e.g.
   * `ScriptStep`'s own "Next" PATCH) — never affects Stop, which stays available whenever a job
   * is genuinely running regardless of this flag. */
  disabled?: boolean;
}

/**
 * The Script/Hear steps' shared job control (SPEC F174.3, F174.4; PLAN T441, T442, T448) — one
 * start/cancel button pair plus progress/error copy, reused verbatim rather than the two steps each
 * carrying a near-identical block. Once `job` is no longer in flight ({@link inFlightJob}), the
 * action button re-enables, no Cancel renders (there is nothing left to cancel), and no progress
 * copy shows. The error renders only when `job.failedKind === kind` (PLAN T465, STORY-435) — each
 * step owns just the failure its own action caused, so a failed preview never surfaces on the
 * script step or vice versa. A legacy row with `error` set but `failedKind` still null (failed
 * before db/47 added the column, which does not backfill it) shows on neither step; that's
 * accepted rather than given a fallback, since the next stamped job clears it.
 */
export function JobRunner({ job, kind, actionLabel, progressLabel, onStart, onCancel, disabled = false }: JobRunnerProps): ReactNode {
  const activeJob = inFlightJob(job);

  return (
    <>
      <div className="flex items-center gap-2">
        <Button type="button" variant="secondary" disabled={activeJob !== null || disabled} onClick={onStart}>
          {actionLabel}
        </Button>
        {activeJob !== null && (
          <Button type="button" variant="secondary" onClick={onCancel}>
            Stop
          </Button>
        )}
      </div>

      {activeJob !== null && (
        <p className="text-[0.82rem] text-mute">
          {activeJob.waitingForStation ? "Waiting for the station to finish talking…" : progressLabel}
        </p>
      )}
      {job !== null && job.error !== null && job.failedKind === kind && (
        <p role="alert" aria-live="assertive" className="text-[0.82rem] text-danger">
          {job.error}
        </p>
      )}
    </>
  );
}
