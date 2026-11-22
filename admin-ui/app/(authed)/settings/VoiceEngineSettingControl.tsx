"use client";

import { type ChangeEvent, type ReactNode } from "react";
import { useVoiceEngines } from "@/lib/use-voice-engines";
import type { SettingControlProps } from "./settings-types";

/** Matches SettingField's shipped single-line control styling. */
const CONTROL_CLASSES =
  "h-9 w-full max-w-md rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

/**
 * `Tts:Engine`'s settings-page control (SPEC F219.2, STORY-555; registered in `SettingsForm`'s
 * per-key control registry, F54.1). Lists the engines the registry holds
 * (`GET /api/voices/engines`) and marks the one speech is rendered with right now. The submitted
 * value is the engine name; the backend rejects an unknown name with a 400 that the form shows.
 *
 * If the list has not loaded (or failed), the current value still gets its own option, so the
 * page never loses the setting and a save never rewrites it by accident.
 */
export function VoiceEngineSettingControl({
  controlId,
  value,
  onChange,
  disabled,
  savedValue,
}: SettingControlProps): ReactNode {
  const state = useVoiceEngines(savedValue);

  const engines = state.kind === "loaded" ? state.data.engines : [];
  const active = state.kind === "loaded" ? state.data.active : null;
  const selected = value === "" ? (active ?? "") : value;
  const selectedIsListed = engines.some((engine) => engine.name === selected);

  return (
    <div className="flex flex-col gap-1">
      <select
        id={controlId}
        value={selected}
        onChange={(e: ChangeEvent<HTMLSelectElement>) => onChange(e.currentTarget.value)}
        disabled={disabled}
        className={CONTROL_CLASSES}
      >
        {selected !== "" && !selectedIsListed && <option value={selected}>{selected}</option>}
        {engines.map((engine) => (
          <option key={engine.name} value={engine.name}>
            {engine.name === active ? `${engine.name} (active)` : engine.name}
          </option>
        ))}
      </select>
      {state.kind === "error" && (
        <p className="text-[0.78rem] text-mute">
          The list of engines could not be loaded. The current choice is kept.
        </p>
      )}
    </div>
  );
}
