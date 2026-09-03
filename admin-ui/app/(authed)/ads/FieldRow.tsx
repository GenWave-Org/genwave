import type { ReactNode } from "react";

// Shared field-label styling + the label/control wrapper for the Ads feature's two editor-shaped
// forms (SPEC F162.1; PLAN T404 review fold c) — `AdSpotEditor` (the spot editor) and
// `BriefsSection` (its own embedded add-brief form) had each grown a byte-identical copy of these
// two class strings and this exact wrapper markup; this is the one shared home, mirroring how
// `FIELD_LABEL_CLASSES`/`FIELD_INPUT_CLASSES` are already a house-wide idiom (`SafeContentClient`,
// `BedPicker`) — just not, until now, shared WITHIN this one feature's own two forms.

export const FIELD_LABEL_CLASSES = "text-[0.82rem] font-semibold text-mute";
export const FIELD_INPUT_CLASSES =
  "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

interface FieldRowProps {
  label: string;
  htmlFor: string;
  children: ReactNode;
}

/** One labeled field — label above the control, the `SafeContentClient`/`BedPicker` layout
 * convention applied as a real component instead of each form hand-rolling the same wrapper. */
export function FieldRow({ label, htmlFor, children }: FieldRowProps): ReactNode {
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={htmlFor} className={FIELD_LABEL_CLASSES}>
        {label}
      </label>
      {children}
    </div>
  );
}
