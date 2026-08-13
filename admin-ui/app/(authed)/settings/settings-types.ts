/**
 * One valid value for a `kind === "choice"` setting, paired with its display label (T175 closes
 * the ruling #3 gap: the server, not the client, owns turning a slug like `cats-whisker` into
 * "Cat's Whisker" — see `ChoiceSettingControl`'s own remarks). `value` is the ONLY part that is
 * ever validated, staged, or PUT back — `label` is presentation only.
 */
export interface SettingChoice {
  value: string;
  label: string;
  /**
   * True for the one choice (if any) this setting resolves to when its staged/stored value is the
   * empty string — for `Station:Theme`, the shipped default (`ThemeCatalog.ShippedDefaultSlug`
   * server-side; see `SettingChoice.IsDefault`'s own remarks in `StationSettingsAllowlist`). T175
   * follow-up: `ChoiceSettingControl` reads this — never a hardcoded theme name — to label the
   * "unset" state distinctly from an actual selection. Optional/falsy for any choice with no such
   * "empty means this" semantics, including every choice on a Choice-kind setting that doesn't
   * define one; the control degrades to a neutral label rather than assuming a default exists.
   */
  isDefault?: boolean;
  /**
   * Provenance stamp (SPEC F103.11, PLAN T187 — mirrors `PersonaDto.importedFrom` verbatim, the
   * station.persona/db-25 pattern applied to the theme kind): the catalog entry's own slug for a
   * catalog-imported theme, `"file"` for a direct upload, or `null` for a shipped default. Read
   * VERBATIM by the badge that renders it — this is provenance, not decoration, so it is never
   * prettified, same rule `PersonasClient`'s own `ProvenanceBadge` follows.
   */
  importedFrom?: string | null;
  /** The moment {@link importedFrom} was last stamped; `null` exactly when `importedFrom` is. */
  importedAt?: string | null;
}

/**
 * Wire shape of one row from `GET /api/settings` (unchanged by the Q9 regroup — SPEC F28.12).
 * `"choice"` is a T163 addition (SPEC F102.14, STORY-265): a value restricted to `choices`, the
 * shipped themes for `Station:Theme` today. `SettingsForm`'s per-key control registry
 * (`SETTING_CONTROL_REGISTRY`) renders it via `ChoiceSettingControl` (T175), the generic control
 * for this kind — kind-based dispatch never has to know about `"choice"` at all.
 */
export interface SettingDto {
  key: string;
  value: string;
  source: "default" | "override";
  applyMode: "live" | "engine-restart" | "enrichment";
  kind: "boolean" | "number" | "number-list" | "string" | "choice";
  unit: string;
  /** The closed set of valid `(value, label)` pairs — present only when `kind` is `"choice"`. */
  choices?: readonly SettingChoice[];
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
  /**
   * The closed set of valid `(value, label)` pairs, straight off {@link SettingDto.choices} —
   * present only for a `kind === "choice"` setting (T175, SPEC F102.14). Optional so every
   * existing registered control (Voice, Corrections, EngineByKind, Audience), none of which read
   * it, is unaffected.
   */
  choices?: readonly SettingChoice[];
}

/**
 * Shape of ASP.NET Core `ValidationProblemDetails` returned on a 400 — every field-naming write
 * failure on this page reads the SAME shape, whether it's `PUT /api/settings`'s batch response
 * (`SettingsForm`) or a dedicated-API control's own single-rule write (`PronunciationRulesControl`,
 * PLAN T145 review should-fix). One shared type here instead of a per-control copy.
 */
export interface ValidationProblemDetails {
  errors: Record<string, string[]>;
  title?: string;
  status?: number;
}

export function isValidationProblemDetails(raw: unknown): raw is ValidationProblemDetails {
  if (typeof raw !== "object" || raw === null) return false;
  const obj = raw as Record<string, unknown>;
  return typeof obj["errors"] === "object" && obj["errors"] !== null;
}
