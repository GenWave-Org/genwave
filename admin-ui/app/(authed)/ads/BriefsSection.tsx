"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { toast } from "@/components/ui/toast";
import { createAdBrief, setAdBriefEnabled, type AdBriefDto } from "@/lib/ads-api";
import type { SponsorRefDto } from "@/lib/sponsors-api";
import { FieldRow, FIELD_INPUT_CLASSES } from "./FieldRow";
import { SponsorPicker } from "./SponsorPicker";

interface BriefsSectionProps {
  briefs: AdBriefDto[];
  /** The rail's current selection (PLAN T447) — preselected in the add form's Sponsor picker;
   * `null` means "All sponsors" and the form opens with no sponsor chosen. */
  sponsorId: number | null;
  /** Every sponsor the add form's picker offers (PLAN T447) — id/name/paused only. */
  sponsors: readonly SponsorRefDto[];
}

type FormStatus = { kind: "idle" } | { kind: "pending" } | { kind: "error"; detail: string };

/**
 * The Briefs tab (SPEC F162.1, F162.2's own upsert key; STORY-392 AC5; PLAN T404) — every brief,
 * pack and owner alike, each with an enable/disable toggle, plus an add form for a new owner
 * brief. Mirrors `AdsSection`'s own client-boundary shape: `page.tsx` renders this directly,
 * `useRouter().refresh()` after every successful mutation (never a local patch).
 *
 * A pack brief is tagged with its `packSlug` (SPEC F162.2's own installed-pack provenance); an
 * owner brief (`packSlug === null`) reads "Owner". The toggle PATCHes ANY brief, pack or owner
 * alike (`AdBriefsController.SetEnabled`'s own reading of F162.1's "enable/disable toggles" — an
 * operator may silence an installed pack brief without uninstalling the pack). The add form creates
 * OWNER briefs only (`POST /api/ad-briefs` never takes a `packSlug`); a duplicate (sponsor, premise)
 * pair 409s with the server's own message shown verbatim (AC5's own "surfaces as 409, not a silent
 * write" demand) — never a second, client-authored wording of the same rule.
 *
 * The add form's own local `formSponsorId` is seeded from `sponsorId` once, on mount, then owned
 * by the form — the rail's selection is URL state, not component state, so `page.tsx` remounts
 * this component with `key={sponsorId ?? "all"}` on a sponsor change (PLAN T447 ruling) rather
 * than this component watching its own `sponsorId` prop for changes after mount.
 */
export function BriefsSection({ briefs, sponsorId, sponsors }: BriefsSectionProps): ReactNode {
  const router = useRouter();
  const onChanged = (): void => router.refresh();

  const [formSponsorId, setFormSponsorId] = useState<number | null>(sponsorId);
  const [premise, setPremise] = useState("");
  const [tone, setTone] = useState("");
  const [structure, setStructure] = useState("");
  const [status, setStatus] = useState<FormStatus>({ kind: "idle" });
  const [pendingToggleId, setPendingToggleId] = useState<number | null>(null);

  const isPending = status.kind === "pending";

  async function handleToggle(brief: AdBriefDto): Promise<void> {
    setPendingToggleId(brief.id);
    const outcome = await setAdBriefEnabled(brief.id, !brief.enabled);
    setPendingToggleId(null);
    if (!outcome.ok) {
      toast.error(outcome.detail);
      return;
    }
    onChanged();
  }

  async function handleAdd(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();

    if (formSponsorId === null) {
      // PLAN T447 ruling: plain wording rather than the server's literal "sponsorId is required."
      // (gh-#707: no wire-level field names in user-visible text).
      setStatus({ kind: "error", detail: "Sponsor is required." });
      return;
    }

    setStatus({ kind: "pending" });
    const outcome = await createAdBrief({
      sponsorId: formSponsorId,
      premise: premise.trim() === "" ? null : premise.trim(),
      tone: tone.trim() === "" ? null : tone.trim(),
      structure: structure.trim() === "" ? null : structure.trim(),
    });

    if (!outcome.ok) {
      setStatus({ kind: "error", detail: outcome.detail });
      return;
    }

    setStatus({ kind: "idle" });
    setFormSponsorId(sponsorId);
    setPremise("");
    setTone("");
    setStructure("");
    toast.success("Brief added.");
    onChanged();
  }

  return (
    <div className="flex flex-col gap-6">
      <section aria-label="Add a brief" className="rounded-[6px] border border-line bg-surface p-5">
        <h2 className="font-display text-[1.1rem] text-ink">Add a brief</h2>

        {status.kind === "error" && (
          <p role="alert" aria-live="assertive" className="mt-3 text-[0.82rem] text-danger">
            {status.detail}
          </p>
        )}

        <form
          onSubmit={(e) => {
            void handleAdd(e);
          }}
          className="mt-4 flex flex-col gap-4"
        >
          <SponsorPicker
            id="brief-sponsor"
            value={formSponsorId}
            sponsors={sponsors}
            disabled={isPending}
            onChange={setFormSponsorId}
          />

          <FieldRow label="Premise" htmlFor="brief-premise">
            <textarea
              id="brief-premise"
              rows={2}
              value={premise}
              onChange={(e) => setPremise(e.currentTarget.value)}
              disabled={isPending}
              className={`${FIELD_INPUT_CLASSES} resize-y py-2`}
            />
          </FieldRow>

          <FieldRow label="Tone" htmlFor="brief-tone">
            <input
              id="brief-tone"
              value={tone}
              onChange={(e) => setTone(e.currentTarget.value)}
              disabled={isPending}
              className={FIELD_INPUT_CLASSES}
            />
          </FieldRow>

          <FieldRow label="Structure" htmlFor="brief-structure">
            <textarea
              id="brief-structure"
              rows={2}
              value={structure}
              onChange={(e) => setStructure(e.currentTarget.value)}
              disabled={isPending}
              className={`${FIELD_INPUT_CLASSES} resize-y py-2`}
            />
          </FieldRow>

          <Button type="submit" disabled={isPending} className="self-start">
            {isPending ? "Adding…" : "Add brief"}
          </Button>
        </form>
      </section>

      <section aria-label="Briefs">
        <h2 className="font-display text-[1.1rem] text-ink">Briefs</h2>

        {briefs.length === 0 ? (
          <p className="mt-3 text-[0.85rem] text-mute">No briefs yet — add one above.</p>
        ) : (
          <div className="mt-3 divide-y divide-line">
            {briefs.map((brief) => (
              <div key={brief.id} className="flex flex-wrap items-center justify-between gap-3 py-3">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <p className="truncate text-[0.9rem] text-ink">{brief.sponsor.name}</p>
                    <Chip title={brief.packSlug ?? undefined}>
                      {brief.packSlug !== null ? `Pack: ${brief.packSlug}` : "Owner"}
                    </Chip>
                  </div>
                  {brief.premise !== null && <p className="truncate text-[0.8rem] text-mute">{brief.premise}</p>}
                </div>

                <label className="flex min-h-10 items-center gap-1.5 text-[0.85rem] text-ink">
                  <input
                    type="checkbox"
                    checked={brief.enabled}
                    aria-label={`Enabled: ${brief.sponsor.name}`}
                    disabled={pendingToggleId === brief.id}
                    onChange={() => {
                      void handleToggle(brief);
                    }}
                  />
                  Enabled
                </label>
              </div>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}
