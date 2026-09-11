import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";

interface StepActionsProps {
  /** Closes the wizard from any step. A spot already created by the Angle & length step stays on
   * the Ads page as a draft — exactly what closing the dialog any other way (Escape, the overlay)
   * already leaves behind; Cancel is the same exit with a name. */
  onCancel: () => void;
  /** Returns to the previous step. Absent on the first step only. */
  onBack?: () => void;
  /** Gates both Cancel and Back exactly as the step's own primary action is gated — never leave a
   * step while its request or the station's job is still in flight. */
  disabled: boolean;
  /** The step's own primary action(s) — Next, or the Approve pair. */
  children: ReactNode;
}

/** The one action row every wizard step ends with (Dean, 2026-09-11 — the wizard had neither a
 * Back nor a Cancel; only Next, Escape, and the overlay): Cancel on the left, Back (when
 * there is a previous step) and the step's own primary action(s) on the right. One component so
 * the five steps can't drift in order, labels, or gating. */
export function StepActions({ onCancel, onBack, disabled, children }: StepActionsProps): ReactNode {
  return (
    <div className="flex flex-wrap items-center justify-between gap-2">
      <Button type="button" variant="secondary" disabled={disabled} onClick={onCancel}>
        Cancel
      </Button>
      <div className="flex flex-wrap items-center gap-2">
        {onBack !== undefined && (
          <Button type="button" variant="secondary" disabled={disabled} onClick={onBack}>
            Back
          </Button>
        )}
        {children}
      </div>
    </div>
  );
}
