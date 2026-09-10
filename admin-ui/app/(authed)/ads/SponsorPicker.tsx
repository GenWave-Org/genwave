import type { ReactNode } from "react";
import type { SponsorRefDto } from "@/lib/sponsors-api";
import { FieldRow, FIELD_INPUT_CLASSES } from "./FieldRow";

// The "Sponsor" picker shared across feature folders (SPEC F171.8, F175.1; PLAN T447, T449
// ruling) — `AdSpotEditor` and `BriefsSection`'s own add-brief form (this folder), plus the Shows
// editor (`../shows/ShowsClient.tsx`), render the ONE `<select>` below (a paused sponsor's label
// suffixed " (paused)", the same "" ↔ `null` coercion) rather than a copy each. Left living in
// this folder rather than moved to `../_components/` (PLAN T449's own call) — it stays a leaf over
// `FieldRow.tsx`, which is itself Ads-scoped, so importing across the one extra folder costs less
// than relocating both; the ads two call sites are unchanged by T449 (both omit `allowNone`).

interface SponsorPickerProps {
  /** The `<select>`'s own id — each call site supplies its own, so `getByLabelText("Sponsor")`
   * still resolves to exactly one control per rendered form. */
  id: string;
  value: number | null;
  sponsors: readonly SponsorRefDto[];
  disabled: boolean;
  onChange: (sponsorId: number | null) => void;
  /** PLAN T449 ruling: an ad spot/brief always names a sponsor, so the placeholder option stays
   * disabled there (both ads call sites omit this prop, defaulting to `false`) — a show does not
   * (SPEC F175.1, `sponsorId` is nullable), so the Shows editor passes `true` and the placeholder
   * becomes a real, selectable "No sponsor" choice instead. */
  allowNone?: boolean;
}

/** One labeled "Sponsor" `<select>` — `value === null` renders the placeholder option (nothing
 * chosen: disabled unless `allowNone`, in which case it reads "No sponsor" and is itself a valid,
 * selectable choice); a paused sponsor's label reads "<name> (paused)" so a picker can flag one
 * without a second round trip (`SponsorRefDto.paused`). */
export function SponsorPicker({
  id,
  value,
  sponsors,
  disabled,
  onChange,
  allowNone = false,
}: SponsorPickerProps): ReactNode {
  return (
    <FieldRow label="Sponsor" htmlFor={id}>
      <select
        id={id}
        value={value ?? ""}
        onChange={(e) => onChange(e.currentTarget.value === "" ? null : Number(e.currentTarget.value))}
        disabled={disabled}
        className={FIELD_INPUT_CLASSES}
      >
        <option value="" disabled={!allowNone}>
          {allowNone ? "No sponsor" : "Choose a sponsor…"}
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
