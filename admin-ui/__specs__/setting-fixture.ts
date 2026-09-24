/**
 * Shared `SettingDto` fixture builder (STORY-478, PLAN T576) — PLAN T574 grew the wire DTO with
 * `label`/`help`/`group`/`min`/`max`, all REQUIRED because the server always sends them. Every
 * spec that builds a `SettingDto` literal by hand needs those five fields too; this is the one
 * place that fills them with innocuous defaults so a fixture that doesn't care about descriptor
 * copy stays a one-line change from before T574, and a fixture that DOES care (AC1-AC4, AC8-AC9 in
 * `settings-descriptor-form.spec.tsx`) overrides only the field it's testing.
 *
 * `label` defaults to the key itself — not a made-up display string — so the house
 * `getByLabelText(new RegExp(key))` idiom every existing settings spec already uses keeps matching
 * unchanged (SettingField renders `setting.label`, and the key IS the label when a fixture doesn't
 * override it).
 */
import type { SettingDto, SettingGroupDto } from "../app/(authed)/settings/settings-types";

const DEFAULT_GROUP: SettingGroupDto = { id: "system", label: "System" };

export function settingDto(
  fields: Pick<SettingDto, "key" | "value" | "source" | "applyMode" | "kind" | "unit"> &
    Partial<SettingDto>
): SettingDto {
  return {
    label: fields.key,
    help: "",
    group: DEFAULT_GROUP,
    min: null,
    max: null,
    ...fields,
  };
}
