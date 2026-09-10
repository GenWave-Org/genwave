// The SpotWizard's own step identity + purpose copy (SPEC F174.1; STORY-421 AC1, AC2; PLAN T448) —
// ONE ordered list both `StepHeader` (the `<ol aria-label="Steps">`) and `SpotWizard` (which panel
// is active) read, so the step SET and its ORDER can never drift between the two. Every `purpose`
// is exactly one sentence — `/^[A-Z][^.]*\.$/` (STORY-421 AC2) — a single period, at the end,
// nothing abbreviated.

export type WizardStepId = "sponsor" | "angle" | "script" | "hear" | "approve";

export interface WizardStepMeta {
  id: WizardStepId;
  label: string;
  purpose: string;
}

export const WIZARD_STEPS: readonly WizardStepMeta[] = [
  {
    id: "sponsor",
    label: "Sponsor",
    purpose: "Choose who this spot is for, or create a new sponsor by typing a name.",
  },
  {
    id: "angle",
    label: "Angle & length",
    purpose: "Choose the angle for this spot and how long it should run.",
  },
  {
    id: "script",
    // F174.6 — an unlabeled line is still read aloud by the announcer voice, not silently dropped;
    // the purpose sentence says so rather than leaving that a surprise at render time.
    label: "Script",
    purpose:
      "Write the script yourself or let the station write it, and know that any unlabeled line will be read by the announcer.",
  },
  {
    id: "hear",
    label: "Hear",
    purpose: "Render a preview and listen to it before you approve this spot.",
  },
  {
    id: "approve",
    label: "Approve",
    purpose: "Approve this spot once you are happy with what you heard.",
  },
] as const satisfies readonly WizardStepMeta[];
