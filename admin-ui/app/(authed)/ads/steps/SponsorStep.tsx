"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { createSponsor } from "@/lib/ads-api";
import type { SponsorRefDto } from "@/lib/sponsors-api";
import { FieldRow, FIELD_INPUT_CLASSES } from "../FieldRow";
import { SponsorPicker } from "../SponsorPicker";

interface SponsorStepProps {
  sponsors: readonly SponsorRefDto[];
  value: number | null;
  onChange: (sponsorId: number | null) => void;
  /** Appends the freshly-created sponsor to the wizard's own working list (PLAN T448) — the list
   * `AdsSection` passed in is a page-load snapshot; this keeps the picker showing it without a
   * round trip back through the server component. */
  onSponsorCreated: (sponsor: SponsorRefDto) => void;
  onError: (detail: string) => void;
  onNext: () => void;
}

/** Lowercase, whitespace-collapsed-then-trimmed — mirrors `station.sponsor_fold(text)`'s own
 * expression (`db/46-sponsor-migration.sh`: `btrim(regexp_replace(lower($1), '\s+', ' ', 'g'))`)
 * closely enough to find the same row the server's own 409 `sponsor_name_taken` just named, without
 * a second round trip to ask which one. */
function foldName(name: string): string {
  return name.toLowerCase().replace(/\s+/g, " ").trim();
}

/**
 * The Sponsor step (SPEC F171.3; STORY-421 AC3; PLAN T448) — pick an existing sponsor from the
 * rail's own list, or type a name the list doesn't have and create it inline. A paused sponsor is
 * still selectable here; the step only names the consequence (the next step's own `POST /api/ads`
 * is where the server actually refuses one, surfaced verbatim there by the shared error banner —
 * this step never re-implements that check).
 */
export function SponsorStep({
  sponsors,
  value,
  onChange,
  onSponsorCreated,
  onError,
  onNext,
}: SponsorStepProps): ReactNode {
  const [typedName, setTypedName] = useState("");
  const [creating, setCreating] = useState(false);

  const trimmedTypedName = typedName.trim();
  const alreadyListed =
    trimmedTypedName !== "" && sponsors.some((sponsor) => foldName(sponsor.name) === foldName(trimmedTypedName));
  const offerCreate = trimmedTypedName !== "" && !alreadyListed;

  const selected = value !== null ? sponsors.find((sponsor) => sponsor.id === value) ?? null : null;

  async function handleCreate(): Promise<void> {
    setCreating(true);
    const outcome = await createSponsor(trimmedTypedName);
    setCreating(false);

    if (outcome.ok) {
      onSponsorCreated(outcome.sponsor);
      onChange(outcome.sponsor.id);
      setTypedName("");
      return;
    }

    if (outcome.type === "sponsor_name_taken") {
      const existing = sponsors.find((sponsor) => foldName(sponsor.name) === foldName(trimmedTypedName));
      if (existing !== undefined) {
        onChange(existing.id);
        setTypedName("");
        return;
      }
    }
    onError(outcome.detail);
  }

  function handleNext(): void {
    if (value === null) {
      onError("Choose a sponsor first.");
      return;
    }
    onNext();
  }

  return (
    <div className="flex flex-col gap-4">
      <SponsorPicker id="wizard-sponsor" value={value} sponsors={sponsors} disabled={creating} onChange={onChange} />

      {selected?.paused === true && (
        <p className="text-[0.78rem] text-mute">
          This sponsor is paused — the station will refuse a new spot for it until it is unpaused.
        </p>
      )}

      <FieldRow label="New sponsor" htmlFor="wizard-sponsor-new">
        <div className="flex gap-2">
          <input
            id="wizard-sponsor-new"
            value={typedName}
            onChange={(e) => setTypedName(e.currentTarget.value)}
            disabled={creating}
            placeholder="Type a name not on the list…"
            className={`${FIELD_INPUT_CLASSES} flex-1`}
          />
          {offerCreate && (
            <Button type="button" variant="secondary" disabled={creating} onClick={() => void handleCreate()}>
              Create
            </Button>
          )}
        </div>
      </FieldRow>

      <div className="flex justify-end">
        <Button type="button" disabled={creating} onClick={handleNext}>
          Next
        </Button>
      </div>
    </div>
  );
}
