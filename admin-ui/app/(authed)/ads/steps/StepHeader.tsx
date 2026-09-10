import type { ReactNode } from "react";
import { WIZARD_STEPS, type WizardStepId } from "./wizard-steps";

interface StepHeaderProps {
  currentStepId: WizardStepId;
}

/** The wizard's own step list (STORY-421 AC1) — an ordered `<ol>` naming every step, the current
 * one marked `aria-current="step"` (the native "which step am I on" affordance screen readers and
 * `getByRole` both already understand, no bespoke ARIA needed). */
export function StepHeader({ currentStepId }: StepHeaderProps): ReactNode {
  return (
    <ol aria-label="Steps" className="flex flex-wrap gap-x-3 gap-y-1 text-[0.78rem] text-mute">
      {WIZARD_STEPS.map((step, index) => (
        <li
          key={step.id}
          aria-current={step.id === currentStepId ? "step" : undefined}
          className={step.id === currentStepId ? "font-semibold text-ink" : undefined}
        >
          {index + 1}. {step.label}
        </li>
      ))}
    </ol>
  );
}
