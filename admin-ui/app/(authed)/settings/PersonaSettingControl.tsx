"use client";

import { type ChangeEvent, type ReactNode } from "react";
import { usePersonaList } from "@/lib/use-persona-list";
import type { SettingControlProps } from "./settings-types";

/** Matches SettingField's shipped single-line control styling — the `VoiceSettingControl`
 * precedent. */
const CONTROL_CLASSES =
  "h-9 w-full max-w-md rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

/** The wire sentinel both `Context:{Weather,History}:PersonaId` share for "no explicit persona" —
 * `SettingValidator`'s own `ContextPersonaIdMin` remarks: null/0 both mean the on-air DJ. GET
 * never returns a literal `null`, only `""` (unset) or a numeric string, so both collapse to this
 * same default option. */
const DEFAULT_PERSONA_VALUE = "0";

/**
 * `Context:Weather:PersonaId`/`Context:History:PersonaId`'s settings-page control (gh-#426;
 * registered in `SettingsForm`'s per-key control-override registry, F54.1). Both keys hold a
 * persona ROW ID as a string on the wire — 0 (or unset) means "the on-air DJ" (SPEC F107.7) — so
 * the shipped plain number input made an operator go look up an id by hand before they could name
 * a persona. This renders the roster by NAME instead; the submitted value is still the id string,
 * the validator's own wire contract is unchanged.
 *
 * Sources the roster from `usePersonaList` (SPEC F79.5 — one `GET /api/personas` listing path for
 * this control, not a second inline fetch; mirrors `VoiceSettingControl`'s own `useVoiceList`
 * precedent after its gh-#426 refactor). A current value the fetched roster doesn't recognize (a
 * deleted persona, or a value staged before the roster loaded) still gets its own option, marked
 * "Unknown persona (#id)" — so simply reopening the page and saving never silently rewrites it to
 * a different persona.
 */
export function PersonaSettingControl({
  controlId,
  value,
  onChange,
  disabled,
}: SettingControlProps): ReactNode {
  const status = usePersonaList();

  const isLoading = status.kind === "loading";
  const personas = status.kind === "loaded" ? status.personas : [];
  const selectedValue = value === "" ? DEFAULT_PERSONA_VALUE : value;
  const currentIsUnknown =
    selectedValue !== DEFAULT_PERSONA_VALUE &&
    !personas.some((persona) => String(persona.id) === selectedValue);

  return (
    <select
      id={controlId}
      value={selectedValue}
      onChange={(e: ChangeEvent<HTMLSelectElement>) => onChange(e.currentTarget.value)}
      disabled={disabled || isLoading}
      className={CONTROL_CLASSES}
    >
      <option value={DEFAULT_PERSONA_VALUE}>On-air DJ (default)</option>
      {currentIsUnknown && (
        <option value={selectedValue}>{`Unknown persona (#${selectedValue})`}</option>
      )}
      {personas.map((persona) => (
        <option key={persona.id} value={String(persona.id)}>
          {persona.name}
        </option>
      ))}
    </select>
  );
}
