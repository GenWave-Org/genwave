"use client";

import type { ChangeEvent, ReactNode } from "react";
import type { SettingControlProps } from "./settings-types";

const EVERYONE_VALUE = "everyone";
const MATURE_VALUE = "mature";

/** Matches SettingField's shipped single-line control styling (text/number inputs, the
 * `VoiceSettingControl` precedent). */
const CONTROL_CLASSES =
  "h-9 w-full max-w-md rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

/**
 * `Station:Audience`'s settings-page control (SPEC F95.1, STORY-250; registered in
 * `SettingsForm`'s per-key control-override registry, F54.1) — replaces T111's generic string
 * input with a two-option dropdown restricted to the only two values
 * `SettingValidator.IsValidAudiencePosture` accepts, mirroring the case-insensitive check there:
 * any staged value other than exactly `"mature"` (case-insensitive) reads and renders as
 * `"everyone"`, the fail-closed default (SPEC F95.1).
 */
export function AudienceSettingControl({
  controlId,
  value,
  onChange,
  disabled,
}: SettingControlProps): ReactNode {
  const isMature = value.trim().toLowerCase() === MATURE_VALUE;

  return (
    <select
      id={controlId}
      value={isMature ? MATURE_VALUE : EVERYONE_VALUE}
      onChange={(e: ChangeEvent<HTMLSelectElement>) => onChange(e.currentTarget.value)}
      disabled={disabled}
      className={CONTROL_CLASSES}
    >
      <option value={EVERYONE_VALUE}>Everyone</option>
      <option value={MATURE_VALUE}>Mature</option>
    </select>
  );
}
