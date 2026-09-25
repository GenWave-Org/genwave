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
 * The descriptor's group — one of `SettingGroup`'s C# members, wire-cased (STORY-478, PLAN T576;
 * SPEC F205.3/F205.4). `id` is the lowercase enum name (`sound`, `voice`, `announcements`,
 * `sponsors`, `library`, `community`, `station`, `system`) and doubles as the future section's
 * anchor id (T577); `label` is the operator-facing group heading, already resolved server-side.
 */
export interface SettingGroupDto {
  id: string;
  label: string;
}

/**
 * Wire shape of one row from `GET /api/settings` (unchanged by the Q9 regroup — SPEC F28.12).
 * `"choice"` is a T163 addition (SPEC F102.14, STORY-265): a value restricted to `choices`, the
 * shipped themes for `Station:Theme` today. `SettingsForm`'s per-key control registry
 * (`SETTING_CONTROL_REGISTRY`) renders it via `ChoiceSettingControl` (T175), the generic control
 * for this kind — kind-based dispatch never has to know about `"choice"` at all.
 *
 * `label`/`help`/`group`/`min`/`max` are PLAN T574's descriptor fields (STORY-477, SPEC F205.3) —
 * the server resolves them (culture-aware label/help/group text, `SettingCeiling` bounds) so the
 * admin UI never carries its own client-side copy table for this text (STORY-478, PLAN T576, SPEC
 * F205.4). They are REQUIRED, not optional: the server always sends them on every row,
 * so a fixture/test double that omits one is a real gap, not backward compatibility to preserve —
 * `__specs__/setting-fixture.ts`'s `settingDto()` fills innocuous defaults for the specs that don't
 * care.
 */
export interface SettingDto {
  key: string;
  value: string;
  source: "default" | "override";
  applyMode: "live" | "engine-restart" | "enrichment";
  kind: "boolean" | "number" | "number-list" | "string" | "choice" | "multi-choice";
  unit: string;
  /** The closed set of valid `(value, label)` pairs — present for `choice` and `multi-choice`. */
  choices?: readonly SettingChoice[];
  /**
   * True when {@link choices} is a live source's last KNOWN-good list, not this request's own
   * attempt (STORY-479, SPEC F205.7f) — the most recent live attempt failed, but an earlier one
   * for the same endpoint (e.g. `Llm:Model`/`Station:Voice`) succeeded. Wire-cased `choicesStale`
   * off the JSON body; `false` unless the server sets it.
   */
  choicesStale?: boolean;
  /**
   * True when a live source behind {@link choices} has no successful attempt to fall back on at
   * all (STORY-479, SPEC F205.7f) — `choices` holds only the saved value, if any (the server
   * appends it as `"<v> (not found)"` when the list doesn't otherwise carry it). Wire-cased
   * `choicesFailed` off the JSON body; `false` unless the server sets it.
   */
  choicesFailed?: boolean;
  /**
   * Optimistic-concurrency token (gh-#486) — the key's currently stored version, `0` when unset.
   * Optional so every fixture/test double that predates gh-#486 keeps compiling unchanged; a caller
   * that omits it when re-submitting the key on `PUT /api/settings` falls back to the pre-gh-#486
   * unconditional last-write-wins write.
   */
  version?: number;
  /** The plain-English field label — rendered in place of the raw key (SPEC F205.4, AC1). */
  label: string;
  /** The help sentence, or `""` when the resx carries none — an empty/whitespace value renders no
   * flyover at all (SPEC F205.4, AC2). */
  help: string;
  /** The section this key belongs to (T577 groups by it; T576 reads only its presence). */
  group: SettingGroupDto;
  /** The number input's `min` attribute, or `null` for no lower bound (SPEC F205.4, AC3). */
  min: number | null;
  /** The number input's `max` attribute, or `null` for no upper bound (SPEC F205.4, AC3). */
  max: number | null;
}

/**
 * RFC 7807 `type` URI a version-guarded `PUT /api/settings` or `/api/pronunciations` write stamps
 * on its 409 body (gh-#486, `GenWave.Host.Api.SettingsProblemTypes.VersionConflict` — kept in sync
 * with that C# constant by hand; both sides are exercised by their own specs) — lets a caller tell
 * "another editor saved first, refetch" apart from any other 409 cause (e.g.
 * `PronunciationRulesControl`'s own duplicate-identity conflict) without parsing `detail` text.
 */
export const SETTINGS_VERSION_CONFLICT_PROBLEM_TYPE = "https://genwave.radio/problems/settings-version-conflict";

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
   * don't surface staging (Choice, Audience) ignore it without ceremony.
   */
  isDirty?: boolean;
  /**
   * The closed set of valid `(value, label)` pairs, straight off {@link SettingDto.choices} —
   * present for a `choice` or `multi-choice` setting (T175, SPEC F102.14). Optional so every
   * existing registered control (Corrections, EngineByKind, Audience), none of which read it, is
   * unaffected.
   */
  choices?: readonly SettingChoice[];
  /** Straight off {@link SettingDto.choicesStale} — see that field's own remarks (STORY-479). */
  choicesStale?: boolean;
  /** Straight off {@link SettingDto.choicesFailed} — see that field's own remarks (STORY-479). */
  choicesFailed?: boolean;
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
