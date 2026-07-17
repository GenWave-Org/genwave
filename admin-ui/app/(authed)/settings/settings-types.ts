/** Wire shape of one row from `GET /api/settings` (unchanged by the Q9 regroup — SPEC F28.12). */
export interface SettingDto {
  key: string;
  value: string;
  source: "default" | "override";
  applyMode: "live" | "engine-restart" | "enrichment";
  kind: "boolean" | "number" | "number-list" | "string";
  unit: string;
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
}
