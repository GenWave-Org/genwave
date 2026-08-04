/**
 * Wire shape of one row from `GET /api/settings` (unchanged by the Q9 regroup — SPEC F28.12).
 * `"choice"` is a T163 addition (SPEC F102.14, STORY-265): a value restricted to `choices`, the
 * shipped theme slugs for `Station:Theme` today. `SettingsForm`'s kind-based dispatch does not
 * yet render it as its own control — it falls through to the plain text branch (no worse than
 * the pre-T163 shape) pending a dedicated closed-choice control, deliberately left to a
 * follow-up task rather than half-built here (see that dispatch's own comment).
 */
export interface SettingDto {
  key: string;
  value: string;
  source: "default" | "override";
  applyMode: "live" | "engine-restart" | "enrichment";
  kind: "boolean" | "number" | "number-list" | "string" | "choice";
  unit: string;
  /** The closed set of valid values — present only when `kind` is `"choice"`. */
  choices?: readonly string[];
}

/**
 * Props shape every per-key control-override registry entry receives (SPEC F54.1). Deliberately
 * narrow and wire-agnostic — a registered control never sees the full `SettingDto` or the form's
 * internals, only the current staged value and a way to change it — so `SettingsForm` stays the
 * only place that knows about dirty-tracking, PUT batching, or validation errors (F54.4).
 */
export interface SettingControlProps {
  /** `id` to pair with the field's existing `<label htmlFor>` — same id SettingField already builds. */
  controlId: string;
  value: string;
  onChange: (value: string) => void;
  disabled: boolean;
  /**
   * True when the staged `value` differs from the last-SAVED value (gh-#139) — computed by
   * SettingField with the exact string comparison the Save diff uses, so a control's "unsaved"
   * indicator can never contradict what Save settings will submit. Optional so controls that
   * don't surface staging (Voice, Audience) ignore it without ceremony.
   */
  isDirty?: boolean;
}
