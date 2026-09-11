"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { createAdBrief, createAdSpot, listAdBriefs, updateAdSpot, type AdBriefDto, type AdSpotDto } from "@/lib/ads-api";
import type { SponsorRefDto } from "@/lib/sponsors-api";
import { FieldRow, FIELD_INPUT_CLASSES, FIELD_LABEL_CLASSES } from "../FieldRow";
import { StepActions } from "./StepActions";

const SPOT_SECONDS_OPTIONS = [15, 30, 60] as const;
const DEFAULT_SPOT_SECONDS = 30;

interface AngleLengthStepProps {
  sponsor: SponsorRefDto;
  /** The spot this wizard already created, when the operator came BACK to this step from Script
   * (or later). Present: "Next" edits that row (`PATCH /api/ads/{id}`) instead of creating a
   * second one, and the fields start from the row's own angle and length. Absent: the first visit
   * — "Next" is the `POST /api/ads` that brings the spot into existence. */
  existingSpot?: AdSpotDto;
  /** Fires with the row "Next" committed — the freshly created spot, the freshly edited one, or
   * `existingSpot` itself untouched when nothing changed (no request is sent then). */
  onSpotCommitted: (spot: AdSpotDto) => void;
  onError: (detail: string) => void;
  onBack: () => void;
  onCancel: () => void;
}

/**
 * The Angle & length step (SPEC F171.6, F174.2; PLAN T448) — an existing brief's premise, or a
 * freshly typed angle, plus the spot's own length; "Next" is this step's own `POST /api/ads`,
 * which is what actually brings the spot into existence (every later step edits that same row).
 * The created spot's title is always `` `${sponsor.name} spot` `` (PLAN T451 ruling, SPEC F171.7)
 * — the same shape `AdSpotWorker.BuildTitle` gives every station-generated spot, so the booth log
 * names the sponsor no matter who made the spot; the angle itself rides in `brief`, never the
 * title. A typed angle can also be kept as a brief for next time (`saveAngleAsBrief`, default on)
 * — a second, best-effort `POST /api/ad-briefs` right after the spot's own create; a 409 there
 * just means today's angle already matches an existing brief for this sponsor, which is fine
 * (nothing left undone), so it is never surfaced as a step failure.
 *
 * Revisited via Back (`existingSpot` present): the row already exists, so "Next" is a PATCH
 * carrying only what changed (`null` = leave unchanged, the `AdSpotSaveBody` contract) and sends
 * nothing at all when nothing did — coming back to look and going forward again never touches the
 * row or its `version`. The brief that matches the row's own angle verbatim is preselected so the
 * step reads back what was chosen, not a blank.
 */
export function AngleLengthStep({
  sponsor,
  existingSpot,
  onSpotCommitted,
  onError,
  onBack,
  onCancel,
}: AngleLengthStepProps): ReactNode {
  const [briefs, setBriefs] = useState<AdBriefDto[] | null>(null);
  const [selectedBriefId, setSelectedBriefId] = useState<number | null>(null);
  const [typedAngle, setTypedAngle] = useState(existingSpot?.brief ?? "");
  const [spotSeconds, setSpotSeconds] = useState<number>(existingSpot?.spotSeconds ?? DEFAULT_SPOT_SECONDS);
  const [saveAngleAsBrief, setSaveAngleAsBrief] = useState(true);
  const [pending, setPending] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const outcome = await listAdBriefs();
      if (cancelled) return;
      if (!outcome.ok) {
        onError(outcome.detail);
        setBriefs([]);
        return;
      }
      const usable = outcome.briefs.filter(
        (brief) => brief.sponsor.id === sponsor.id && brief.enabled && brief.premise !== null
      );
      setBriefs(usable);
      // Back from a later step: the brief whose premise IS the row's angle reads back as chosen.
      const matching = existingSpot === undefined ? undefined : usable.find((brief) => brief.premise === existingSpot.brief);
      if (matching !== undefined) setSelectedBriefId(matching.id);
    })();
    return () => {
      cancelled = true;
    };
    // Runs once, at mount — the sponsor and the revisited row are fixed for this step's whole
    // lifetime (the wizard never reopens this step against a different sponsor without remounting
    // it).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const usingTypedAngle = selectedBriefId === null;

  async function handleNext(): Promise<void> {
    const brief = selectedBriefId !== null ? briefs?.find((b) => b.id === selectedBriefId) ?? null : null;
    const angleText = brief !== null ? (brief.premise ?? "") : typedAngle.trim();
    if (angleText === "") {
      onError("Choose an angle from the list, or type one.");
      return;
    }

    const edit = existingSpot === undefined ? undefined : editSpot(existingSpot, angleText);
    if (existingSpot !== undefined && edit === null) {
      // Nothing changed on a revisit — no request, the row and its version are exactly as they were.
      onSpotCommitted(existingSpot);
      return;
    }

    setPending(true);
    const outcome = edit === undefined || edit === null ? await createSpot(angleText) : await edit;
    if (!outcome.ok) {
      setPending(false);
      onError(outcome.detail);
      return;
    }

    if (brief === null && saveAngleAsBrief) {
      // 409 (a duplicate premise for this sponsor already exists) is fine — nothing left undone.
      await createAdBrief({ sponsorId: sponsor.id, premise: angleText, tone: null, structure: null });
    }

    setPending(false);
    onSpotCommitted(outcome.spot);
  }

  function createSpot(angleText: string): ReturnType<typeof createAdSpot> {
    return createAdSpot({
      sponsorId: sponsor.id,
      title: `${sponsor.name} spot`,
      brief: angleText,
      script: null,
      voicePlan: null,
      spotSeconds,
      bedMediaId: null,
    });
  }

  /** `null` when neither the angle nor the length differs from the row — the caller sends nothing. */
  function editSpot(spot: AdSpotDto, angleText: string): ReturnType<typeof updateAdSpot> | null {
    const briefChanged = angleText !== (spot.brief ?? "");
    const secondsChanged = spotSeconds !== spot.spotSeconds;
    if (!briefChanged && !secondsChanged) return null;
    return updateAdSpot(spot.id, spot.version, {
      sponsorId: null,
      title: null,
      brief: briefChanged ? angleText : null,
      script: null,
      voicePlan: null,
      spotSeconds: secondsChanged ? spotSeconds : null,
      bedMediaId: null,
    });
  }

  return (
    <div className="flex flex-col gap-4">
      {briefs !== null && briefs.length > 0 && (
        <fieldset className="flex flex-col gap-2">
          <legend className={FIELD_LABEL_CLASSES}>Angle</legend>
          <div role="radiogroup" aria-label="Angle" className="flex flex-col gap-2">
            {briefs.map((brief) => (
              <label key={brief.id} className="flex items-start gap-2 text-[0.85rem] text-ink">
                <input
                  type="radio"
                  name="wizard-angle-brief"
                  checked={selectedBriefId === brief.id}
                  disabled={pending}
                  onChange={() => setSelectedBriefId(brief.id)}
                />
                {brief.premise}
              </label>
            ))}
            <label className="flex items-start gap-2 text-[0.85rem] text-ink">
              <input
                type="radio"
                name="wizard-angle-brief"
                checked={usingTypedAngle}
                disabled={pending}
                onChange={() => setSelectedBriefId(null)}
              />
              Write a new angle
            </label>
          </div>
        </fieldset>
      )}

      {usingTypedAngle && (
        <FieldRow label="Angle" htmlFor="wizard-angle-text">
          <textarea
            id="wizard-angle-text"
            rows={3}
            value={typedAngle}
            onChange={(e) => setTypedAngle(e.currentTarget.value)}
            disabled={pending}
            placeholder="Premise, tone, structure — a writing hint, never itself airable."
            className={`${FIELD_INPUT_CLASSES} resize-y py-2`}
          />
        </FieldRow>
      )}

      {usingTypedAngle && (
        <label className="flex items-center gap-2 text-[0.82rem] text-ink">
          <input
            type="checkbox"
            checked={saveAngleAsBrief}
            disabled={pending}
            onChange={(e) => setSaveAngleAsBrief(e.currentTarget.checked)}
          />
          Keep this angle for later
        </label>
      )}

      <fieldset className="flex flex-col gap-2">
        <legend className={FIELD_LABEL_CLASSES}>Length</legend>
        <div role="radiogroup" aria-label="Length" className="flex gap-2">
          {SPOT_SECONDS_OPTIONS.map((seconds) => (
            <label
              key={seconds}
              className="flex h-9 items-center gap-1.5 rounded-[6px] border border-line px-3 text-[0.85rem] text-ink has-[:checked]:border-accent"
            >
              <input
                type="radio"
                name="wizard-length"
                checked={spotSeconds === seconds}
                disabled={pending}
                onChange={() => setSpotSeconds(seconds)}
              />
              {seconds}s
            </label>
          ))}
        </div>
      </fieldset>

      <StepActions onCancel={onCancel} onBack={onBack} disabled={pending}>
        <Button type="button" disabled={pending} onClick={() => void handleNext()}>
          {pending ? (existingSpot === undefined ? "Creating…" : "Saving…") : "Next"}
        </Button>
      </StepActions>
    </div>
  );
}
