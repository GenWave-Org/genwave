"use client";

import { useEffect, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import * as Dialog from "@radix-ui/react-dialog";
import { useRestoreFocus } from "@/lib/use-restore-focus";
import { cancelSpotJob, fetchAdSpot, inFlightJob, previewSpot, writeSpot, type AdSpotDto } from "@/lib/ads-api";
import type { SponsorRefDto } from "@/lib/sponsors-api";
import { AngleLengthStep } from "./steps/AngleLengthStep";
import { ApproveStep } from "./steps/ApproveStep";
import { HearStep } from "./steps/HearStep";
import { ScriptStep } from "./steps/ScriptStep";
import { SponsorStep } from "./steps/SponsorStep";
import { StepHeader } from "./steps/StepHeader";
import { WIZARD_STEPS, type WizardStepId } from "./steps/wizard-steps";

const JOB_POLL_INTERVAL_MS = 2000;

export interface SpotWizardProps {
  /** Every sponsor the Sponsor step's own picker offers (PLAN T447) — id/name/paused only; a
   * sponsor created inline mid-wizard is appended to this list locally, never re-fetched. */
  sponsors: readonly SponsorRefDto[];
  /** The rail's current selection (PLAN T447), preselected in the Sponsor step; `null` leaves it
   * unchosen. */
  initialSponsorId: number | null;
  onClose: () => void;
}

/**
 * The five-step "New spot" wizard (SPEC F171.3, F174.1–F174.7; STORY-421, STORY-427; PLAN T448) —
 * Sponsor, Angle & length, Script, Hear, Approve, in that fixed order (`WIZARD_STEPS`). Replaces
 * `AdSpotEditor`'s own free-text create path entirely (`AdsSection` now renders this instead, for
 * `editing === "new"`); `AdSpotEditor` itself now only ever renders for EDITING an existing row
 * (PLAN T448 ruling — its own create branch is gone).
 *
 * One piece of mutable truth (`spot`) once the Angle & length step creates the row — every later
 * step reads and writes that same object, refetched fresh after every mutation
 * (`AdsSection.onChanged`'s own re-fetch-never-patch law, applied here to one row instead of a
 * list). The 2 s write/preview job poll ({@link JOB_POLL_INTERVAL_MS}) lives here, one `useEffect`
 * shared by the Script and Hear steps (both can start a job; only one can ever be running at a
 * time — `AdSpotJobService` is one job station-wide), keyed on a derived primitive
 * (`activeSpotId`) rather than the `job` object itself so a poll tick's own fresh-but-equal object
 * never tears the interval down and rebuilds it.
 */
export function SpotWizard({ sponsors, initialSponsorId, onClose }: SpotWizardProps): ReactNode {
  const router = useRouter();
  const restoreFocus = useRestoreFocus("on-mount");

  const [stepIndex, setStepIndex] = useState(0);
  const [localSponsors, setLocalSponsors] = useState<readonly SponsorRefDto[]>(sponsors);
  const [sponsorId, setSponsorId] = useState<number | null>(initialSponsorId);
  const [spot, setSpot] = useState<AdSpotDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  const currentStep = WIZARD_STEPS[stepIndex];
  // The Angle & length step's own sponsor object (its title needs `sponsor.name`, PLAN T451
  // ruling) — resolved from `localSponsors` rather than carried as a second piece of state, so a
  // sponsor created inline mid-wizard (`onSponsorCreated` below) is found the same way as one
  // chosen from the original list.
  const sponsor = sponsorId === null ? undefined : localSponsors.find((s) => s.id === sponsorId);

  // `WIZARD_STEPS.findIndex` keeps every "advance" call site keyed on the step it is LEAVING
  // (a `WizardStepId`) rather than a hardcoded next-index literal that would silently drift the
  // moment a step is inserted, removed, or reordered in `wizard-steps.ts`.
  function goToNext(fromId: WizardStepId): void {
    setError(null);
    setStepIndex(WIZARD_STEPS.findIndex((step) => step.id === fromId) + 1);
  }

  function applySpot(next: AdSpotDto): void {
    setSpot(next);
    setError(null);
  }

  // The write/preview job poll — active exactly while the current spot has a job in flight
  // ({@link inFlightJob}), regardless of which step (Script or Hear) is on screen.
  const activeSpotId = spot !== null && inFlightJob(spot.job) !== null ? spot.id : null;
  useEffect(() => {
    if (activeSpotId === null) return;
    const intervalId = setInterval(() => {
      void (async () => {
        const outcome = await fetchAdSpot(activeSpotId);
        if (outcome.ok) applySpot(outcome.spot);
        else setError(outcome.detail);
      })();
    }, JOB_POLL_INTERVAL_MS);
    return () => clearInterval(intervalId);
  }, [activeSpotId]);

  async function handleStartJob(kind: "write" | "preview"): Promise<void> {
    if (spot === null) return;
    const outcome = kind === "write" ? await writeSpot(spot.id) : await previewSpot(spot.id);
    if (!outcome.ok) {
      setError(outcome.detail);
      return;
    }
    applySpot(outcome.spot);
  }

  async function handleCancelJob(): Promise<void> {
    if (spot === null) return;
    const outcome = await cancelSpotJob(spot.id);
    if (!outcome.ok) {
      setError(outcome.detail);
      return;
    }
    const refreshed = await fetchAdSpot(spot.id);
    if (refreshed.ok) applySpot(refreshed.spot);
    else setError(refreshed.detail);
  }

  function handleApproved(approvedSpot: AdSpotDto): void {
    applySpot(approvedSpot);
    router.refresh();
    onClose();
  }

  if (currentStep === undefined) {
    // `stepIndex` only ever holds a `WIZARD_STEPS` position (`useState(0)`, or `goToNext`'s own
    // `WIZARD_STEPS.findIndex(...) + 1`) — `noUncheckedIndexedAccess`'s own honesty about a plain
    // array index, not a real runtime possibility. Every hook above has already run unconditionally
    // by this point, so this guard sits after them rather than short-circuiting the component body
    // early (Rules of Hooks).
    return null;
  }

  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none" />
        <Dialog.Content
          className="fixed left-1/2 top-1/2 z-50 flex max-h-[85vh] w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 flex-col overflow-y-auto rounded-[6px] border border-line bg-surface p-6 transition-opacity duration-200 ease-out focus:outline-none motion-reduce:transition-none"
          onCloseAutoFocus={restoreFocus.onCloseAutoFocus}
        >
          <Dialog.Title className="font-display text-[1.1rem] text-ink">New spot</Dialog.Title>

          <div className="mt-3">
            <StepHeader currentStepId={currentStep.id} />
          </div>

          <p data-purpose className="mt-2 text-[0.85rem] text-mute">
            {currentStep.purpose}
          </p>

          {error !== null && (
            <p role="alert" aria-live="assertive" className="mt-3 text-[0.82rem] text-danger">
              {error}
            </p>
          )}

          <div className="mt-4 flex flex-col gap-4">
            {currentStep.id === "sponsor" && (
              <SponsorStep
                sponsors={localSponsors}
                value={sponsorId}
                onChange={setSponsorId}
                onSponsorCreated={(sponsor) => setLocalSponsors((prev) => [...prev, sponsor])}
                onError={setError}
                onNext={() => goToNext("sponsor")}
              />
            )}

            {currentStep.id === "angle" && sponsor !== undefined && (
              <AngleLengthStep
                sponsor={sponsor}
                onSpotCreated={(created) => {
                  applySpot(created);
                  goToNext("angle");
                }}
                onError={setError}
              />
            )}

            {currentStep.id === "script" && spot !== null && (
              <ScriptStep
                spot={spot}
                onSpotUpdated={applySpot}
                onError={setError}
                onNext={() => goToNext("script")}
                onWrite={() => void handleStartJob("write")}
                onCancelJob={() => void handleCancelJob()}
              />
            )}

            {currentStep.id === "hear" && spot !== null && (
              <HearStep
                spot={spot}
                onSpotUpdated={applySpot}
                onError={setError}
                onNext={() => goToNext("hear")}
                onPreview={() => void handleStartJob("preview")}
                onCancelJob={() => void handleCancelJob()}
              />
            )}

            {currentStep.id === "approve" && spot !== null && (
              <ApproveStep spot={spot} onApproved={handleApproved} onSpotUpdated={applySpot} onError={setError} />
            )}
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
