"use client";

import { type ChangeEvent, type ReactNode } from "react";
import { useVoiceEngines } from "@/lib/use-voice-engines";
import type { SettingControlProps } from "./settings-types";

const INPUT_CLASSES =
  "h-9 w-24 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

type SecondsByEngine = Record<string, number>;

/**
 * Reads the stored JSON object, keyed by the registry's engine names. Stored keys match engines
 * ignoring case (the backend does); keys that match no engine are dropped, so saving removes them.
 * Anything that is not a `{name: number}` object counts as "no limits set".
 */
function parseLimits(value: string, engineNames: readonly string[]): SecondsByEngine {
  if (value.trim() === "") return {};
  try {
    const parsed: unknown = JSON.parse(value);
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) return {};
    const limits: SecondsByEngine = {};
    for (const [key, seconds] of Object.entries(parsed)) {
      const name = engineNames.find((n) => n.toLowerCase() === key.toLowerCase());
      if (name !== undefined && typeof seconds === "number") limits[name] = seconds;
    }
    return limits;
  } catch {
    return {};
  }
}

/** The JSON object of the filled-in limits, or "" when none are (the setting's "no overrides" value). */
function serializeLimits(limits: SecondsByEngine): string {
  return Object.keys(limits).length === 0 ? "" : JSON.stringify(limits);
}

// Bounds mirror VoiceEnginesOptionsValidator.MinRenderBudgetSeconds / MaxRenderBudgetSeconds (GenWave.Tts).
const MIN_SECONDS = 1;
const MAX_SECONDS = 600;

/**
 * `Tts:EngineRenderBudgets`'s settings-page control (SPEC F219.6, T750a; registered in
 * `SettingsForm`'s per-key control registry, F54.1). One seconds field per voice engine the
 * registry holds (`GET /api/voices/engines`); an empty field means that engine keeps its own
 * limit, and the field's placeholder shows that own limit (not any saved override). The submitted value is the JSON
 * object of the filled-in fields, or "" when all are empty. The backend range-checks it and the
 * form shows its 400.
 */
export function EngineRenderBudgetsSettingControl({
  controlId,
  value,
  onChange,
  disabled,
  savedValue,
}: SettingControlProps): ReactNode {
  const state = useVoiceEngines(savedValue);
  const engines = state.kind === "loaded" ? state.data.engines : [];
  const limits = parseLimits(value, engines.map((e) => e.name));

  function setLimit(name: string, raw: string): void {
    const next = { ...limits };
    const seconds = Number(raw);
    if (raw.trim() === "" || !Number.isFinite(seconds)) {
      delete next[name];
    } else {
      next[name] = seconds;
    }
    onChange(serializeLimits(next));
  }

  return (
    <div id={controlId} className="flex flex-col gap-2">
      {engines.map((engine) => (
        <label key={engine.name} className="flex items-center gap-2 text-[0.85rem] text-ink">
          <input
            type="number"
            min={MIN_SECONDS}
            max={MAX_SECONDS}
            step={1}
            inputMode="numeric"
            aria-label={`${engine.name} time limit in seconds`}
            value={limits[engine.name] ?? ""}
            placeholder={String(engine.configRenderBudgetSeconds)}
            onChange={(e: ChangeEvent<HTMLInputElement>) => setLimit(engine.name, e.currentTarget.value)}
            disabled={disabled}
            className={INPUT_CLASSES}
          />
          <span>{engine.name} (seconds)</span>
        </label>
      ))}
      {state.kind === "error" && (
        <p className="text-[0.78rem] text-mute">
          The list of engines could not be loaded. The saved limits are kept.
        </p>
      )}
    </div>
  );
}
