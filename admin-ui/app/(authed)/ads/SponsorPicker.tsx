import type { ReactNode } from "react";
import type { SponsorRefDto } from "@/lib/sponsors-api";
import { FieldRow, FIELD_INPUT_CLASSES } from "./FieldRow";

// The "Sponsor" picker shared by the Ads feature's two editor-shaped forms (SPEC F171.8; PLAN
// T447 ruling) — `AdSpotEditor` and `BriefsSection`'s own add-brief form render the ONE `<select>`
// below (placeholder option, a paused sponsor's label suffixed " (paused)", the same "" ↔ `null`
// coercion) rather than a copy each, mirroring `FieldRow.tsx`'s own precedent for this feature's
// two forms.

interface SponsorPickerProps {
  /** The `<select>`'s own id — each call site supplies its own, so `getByLabelText("Sponsor")`
   * still resolves to exactly one control per rendered form. */
  id: string;
  value: number | null;
  sponsors: readonly SponsorRefDto[];
  disabled: boolean;
  onChange: (sponsorId: number | null) => void;
}

/** One labeled "Sponsor" `<select>` — `value === null` renders the disabled placeholder option
 * (nothing chosen); a paused sponsor's label reads "<name> (paused)" so a picker can flag one
 * without a second round trip (`SponsorRefDto.paused`). */
export function SponsorPicker({ id, value, sponsors, disabled, onChange }: SponsorPickerProps): ReactNode {
  return (
    <FieldRow label="Sponsor" htmlFor={id}>
      <select
        id={id}
        value={value ?? ""}
        onChange={(e) => onChange(e.currentTarget.value === "" ? null : Number(e.currentTarget.value))}
        disabled={disabled}
        className={FIELD_INPUT_CLASSES}
      >
        <option value="" disabled>
          Choose a sponsor…
        </option>
        {sponsors.map((sponsor) => (
          <option key={sponsor.id} value={sponsor.id}>
            {sponsor.name}
            {sponsor.paused ? " (paused)" : ""}
          </option>
        ))}
      </select>
    </FieldRow>
  );
}
