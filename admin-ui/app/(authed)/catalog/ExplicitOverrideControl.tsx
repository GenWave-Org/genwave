"use client";

import { useState, type ChangeEvent, type ReactNode } from "react";
import { toast } from "@/components/ui/toast";
import { describeRatingFailure, setExplicitOverride } from "@/lib/broadcast-api";

export interface ExplicitOverrideChange {
  explicit: boolean | null;
  explicitSource: string | null;
}

export interface ExplicitOverrideControlProps {
  mediaId: string;
  /** The row's title, falling back to the mediaId — feeds the control's accessible name. */
  rowLabel: string;
  explicit: boolean | null | undefined;
  /** Called with the PUT response's fresh explicit/explicitSource pair on success (mirrors
   * NeverPlayControl's "no refetch" posture, F33.11) — the caller folds this into the row's local
   * state. */
  onChange: (next: ExplicitOverrideChange) => void;
}

/** Wire values for each option — a native `<select>` option value is always a string, so the
 * tri-state `null` (clear-to-unknown) is represented as `""`. */
const UNKNOWN_VALUE = "";
const EXPLICIT_VALUE = "true";
const CLEAN_VALUE = "false";

function toOptionValue(explicit: boolean | null | undefined): string {
  if (explicit === true) return EXPLICIT_VALUE;
  if (explicit === false) return CLEAN_VALUE;
  return UNKNOWN_VALUE;
}

function fromOptionValue(value: string): boolean | null {
  if (value === EXPLICIT_VALUE) return true;
  if (value === CLEAN_VALUE) return false;
  return null;
}

/**
 * Operator override for one catalog row's explicit classification (SPEC F95.3, F95.5, STORY-251,
 * PLAN T116). Sits in the same catalog-row cell as the Explicit badge, mirroring the badge+control
 * layout the Rating column already uses for the never-play verdict — `NeverPlayControl`'s actual
 * control lives in the catalog table (not the row detail page), so this override matches that
 * placement rather than the detail page's edit form.
 *
 * A tri-state `<select>` rather than three separate buttons: "Unknown"/"Explicit"/"Clean" map
 * directly onto the wire's `null`/`true`/`false`, and a native select is the most compact affordance
 * for three mutually-exclusive states inside a table cell (the same `<select>` idiom
 * `MoveToLibraryAction`/`SettingField` already use elsewhere in this app). Commits immediately on
 * change — no separate Save step, matching `NeverPlayControl`'s immediate-commit idiom (this
 * write is ETag-free and idempotent by design — see `ExplicitOverrideController`'s doc comment).
 *
 * That immediate-commit parity does NOT extend to how the pending window renders: `NeverPlayControl`
 * is an icon button whose appearance is driven only by the *committed* `neverPlay` flag, so staying
 * unchanged (just disabled) while its PUT is in flight reads as neutral. This control's appearance
 * IS the value the operator picked — a controlled `<select>` bound straight to the server-truth
 * `explicit` prop would re-render to the stale option the instant `pending` flips, visibly snapping
 * the pick back before jumping forward again on success. So `pendingValue` holds the picked option
 * locally for the pending window (cleared on both success and failure) — a select's bound value
 * can't get away with the icon button's "leave it alone" posture.
 */
export function ExplicitOverrideControl({
  mediaId,
  rowLabel,
  explicit,
  onChange,
}: ExplicitOverrideControlProps): ReactNode {
  const [pending, setPending] = useState(false);
  const [pendingValue, setPendingValue] = useState<string | null>(null);

  async function handleChange(e: ChangeEvent<HTMLSelectElement>): Promise<void> {
    const picked = e.currentTarget.value;
    const next = fromOptionValue(picked);
    setPendingValue(picked);
    setPending(true);
    const outcome = await setExplicitOverride(mediaId, next);
    setPending(false);
    setPendingValue(null);
    if (outcome.ok) {
      onChange({ explicit: outcome.explicit, explicitSource: outcome.explicitSource });
      return;
    }
    toast.error(describeRatingFailure(outcome.kind, outcome.status));
  }

  return (
    <select
      aria-label={`Explicit override for ${rowLabel}`}
      value={pendingValue ?? toOptionValue(explicit)}
      onChange={(e) => {
        void handleChange(e);
      }}
      disabled={pending}
      className="h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50"
    >
      <option value={UNKNOWN_VALUE}>Unknown</option>
      <option value={EXPLICIT_VALUE}>Explicit</option>
      <option value={CLEAN_VALUE}>Clean</option>
    </select>
  );
}
