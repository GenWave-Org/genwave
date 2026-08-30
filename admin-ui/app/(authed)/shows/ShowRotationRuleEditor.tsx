"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { toast } from "@/components/ui/toast";
import { usePoll } from "@/lib/use-poll";
import {
  fetchShowRotationStatus,
  saveShowRotation,
  type RotationPredicateDto,
  type ShowRotationStatus,
} from "@/lib/shows-rotation-api";

export interface ShowRotationRuleEditorProps {
  showId: number;
  /** The show's own rotation rule as of the last successful GET/PUT (SPEC F152.5) — `null` means
   * no rule. This component owns every SUBSEQUENT edit itself; the parent only ever seeds the
   * initial value. */
  initialRotation: RotationPredicateDto | null;
  /** How often the pool/last-airing status re-polls, in ms — a test-only override; production
   * omits this and gets the gentle default (mirrors ShowsClient's own `timeZone` injection idiom).
   * Deliberately slower than the dashboard's 5s default (SPEC F28.7/F28.8): this is a per-card
   * background fact, never a live-updating headline number. */
  pollIntervalMs?: number;
}

interface FormValues {
  maxPlays: string;
  notAiredWithinDays: string;
}

function formValuesFrom(rotation: RotationPredicateDto | null): FormValues {
  return {
    maxPlays: rotation?.maxPlays?.toString() ?? "",
    notAiredWithinDays: rotation?.notAiredWithinDays?.toString() ?? "",
  };
}

/** Parses the form's two text fields into a save payload, or `null` when both are blank — the
 * caller (`handleSave`) reads a `null` result as "nothing to save" and refuses client-side before
 * ever reaching the wire (mirrors SPEC F115.1's own "UI prevents the round-trip" posture for the
 * name/tagline/flavor budgets one component over); a genuinely invalid NUMBER (negative maxPlays,
 * an out-of-range notAiredWithinDays) is deliberately left to the server's own 400 — the field's
 * `min`/`max` attributes already discourage it at the DOM level, and re-deriving that bound check
 * here would be a second copy of SPEC F152.5's own three validation rules to keep in sync. */
function rotationFrom(form: FormValues): RotationPredicateDto | null {
  const maxPlays = form.maxPlays.trim() === "" ? null : Number(form.maxPlays);
  const notAiredWithinDays = form.notAiredWithinDays.trim() === "" ? null : Number(form.notAiredWithinDays);
  return maxPlays === null && notAiredWithinDays === null ? null : { maxPlays, notAiredWithinDays };
}

/** "1,234 tracks eligible right now" / "eligibility unknown" (SPEC F152.5) — the live pool size
 * chip, thousands-grouped (design-aesthetic's tabular-numbers rule) so a four-digit count doesn't
 * misread as two two-digit runs. */
function poolLabel(status: ShowRotationStatus | null): string {
  if (status?.pool?.eligible == null) return "eligibility unknown";
  return `${status.pool.eligible.toLocaleString()} track${status.pool.eligible === 1 ? "" : "s"} eligible right now`;
}

/** "last airing: 4 picks, 2 relaxed" (SPEC F152.5, STORY-373 AC3) — omitted entirely (not "last
 * airing: none") when the show has never aired: `airedCount`/`relaxed` both `null` (T362 review
 * LOW-6's rename; the server now always answers 200, so `status?.lastAiring` itself is only ever
 * `null` on a genuine fetch failure — the "never aired" case is a VALUE inside a present object,
 * checked here on `airedCount` specifically) means a show with no rotation history yet has nothing
 * to report, not a zero to report. */
function LastAiringLine({ status }: { status: ShowRotationStatus | null }): ReactNode {
  const lastAiring = status?.lastAiring;
  if (lastAiring?.airedCount == null) return null;
  return (
    <p className="text-[0.78rem] text-mute">
      last airing: <span className="tabular-nums">{lastAiring.airedCount}</span> picks,{" "}
      <span className="tabular-nums">{lastAiring.relaxed}</span> relaxed
    </p>
  );
}

// Gentle cadence (SPEC F152.5's own "live pool size" framing, ARCHITECTURE.md's Announcements-page
// reuse note) — a per-card background fact, not a live headline number, so this stays well slower
// than usePoll's own 5s dashboard default.
const DEFAULT_POLL_INTERVAL_MS = 20_000;

const FIELD_LABEL_CLASSES = "text-[0.78rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES =
  "h-9 w-28 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink tabular-nums disabled:opacity-50";

/**
 * The Shows page's own rotation rule editor (SPEC F152.5, STORY-373, PLAN T362) — one per show
 * card: two numeric inputs (`maxPlays`/`notAiredWithinDays`), a save action, a clear action, the
 * live pool-size chip, and the last-airing line. Follows the Announcements page template
 * (ARCHITECTURE.md's own Gardener reuse map): a never-throw `@/lib/shows-rotation-api` module and
 * `usePoll` for the chip/line, at a gentle (not dashboard-speed) cadence.
 *
 * <b>Two independent write surfaces, deliberately.</b> Saving name/tagline/flavor
 * (`ShowsClient.handleSubmit`, `PATCH /api/shows/{slug}`) and saving the rotation rule (THIS
 * component, `PUT /api/shows/{id}`) are two different endpoints with two different bodies — this
 * component never folds into `ShowsClient`'s own create/edit form, mirroring how the two write
 * paths are two different `IShowStore` methods on the server (`UpdateAsync` vs `SetRotationAsync`).
 */
export function ShowRotationRuleEditor({
  showId,
  initialRotation,
  pollIntervalMs,
}: ShowRotationRuleEditorProps): ReactNode {
  const [rotation, setRotation] = useState<RotationPredicateDto | null>(initialRotation);
  const [form, setForm] = useState<FormValues>(() => formValuesFrom(initialRotation));
  const [isSaving, setIsSaving] = useState(false);

  const { data: status } = usePoll(() => fetchShowRotationStatus(showId), {
    intervalMs: pollIntervalMs ?? DEFAULT_POLL_INTERVAL_MS,
  });

  /** The one PUT + state-update path both the save and clear actions ride (T362 review LOW-7 —
   * the two used to duplicate this identical five-line body). `successMessage` is the only thing
   * that ever varied between them. */
  async function applyRotation(next: RotationPredicateDto | null, successMessage: string): Promise<void> {
    setIsSaving(true);
    const outcome = await saveShowRotation(showId, next);
    if (outcome.ok) {
      setRotation(outcome.rotation);
      setForm(formValuesFrom(outcome.rotation));
      toast.success(successMessage);
    } else {
      toast.error(outcome.detail);
    }
    setIsSaving(false);
  }

  async function handleSave(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    const next = rotationFrom(form);
    if (next === null) {
      toast.error("Set at least one of Max plays or Not aired within days.");
      return;
    }
    await applyRotation(next, "Rotation rule saved.");
  }

  async function handleClear(): Promise<void> {
    await applyRotation(null, "Rotation rule cleared.");
  }

  return (
    <div className="mt-3 flex flex-col gap-2 border-t border-line pt-3">
      <form
        aria-label="Rotation rule"
        onSubmit={(e) => {
          void handleSave(e);
        }}
        className="flex flex-wrap items-end gap-3"
      >
        <div className="flex flex-col gap-1">
          <label htmlFor={`rotation-max-plays-${showId}`} className={FIELD_LABEL_CLASSES}>
            Max plays
          </label>
          <input
            id={`rotation-max-plays-${showId}`}
            type="number"
            min={0}
            value={form.maxPlays}
            onChange={(e) => {
              const maxPlays = e.currentTarget.value;
              setForm((prev) => ({ ...prev, maxPlays }));
            }}
            disabled={isSaving}
            className={FIELD_INPUT_CLASSES}
          />
        </div>

        <div className="flex flex-col gap-1">
          <label htmlFor={`rotation-not-aired-${showId}`} className={FIELD_LABEL_CLASSES}>
            Not aired within (days)
          </label>
          <input
            id={`rotation-not-aired-${showId}`}
            type="number"
            min={1}
            max={3650}
            value={form.notAiredWithinDays}
            onChange={(e) => {
              const notAiredWithinDays = e.currentTarget.value;
              setForm((prev) => ({ ...prev, notAiredWithinDays }));
            }}
            disabled={isSaving}
            className={FIELD_INPUT_CLASSES}
          />
        </div>

        <Button type="submit" variant="secondary" disabled={isSaving}>
          {isSaving ? "Saving…" : "Save rule"}
        </Button>
        {rotation !== null && (
          <Button
            type="button"
            variant="secondary"
            disabled={isSaving}
            onClick={() => {
              void handleClear();
            }}
          >
            Clear rule
          </Button>
        )}
        <Chip>{poolLabel(status)}</Chip>
      </form>
      <LastAiringLine status={status} />
    </div>
  );
}
