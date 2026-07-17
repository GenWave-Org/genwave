"use client";

import { useEffect, useState, type ChangeEvent, type ReactNode } from "react";
import type { PersonaDto } from "../personas/types";
import type { SettingControlProps } from "./settings-types";

type PersonaListStatus =
  | { kind: "loading" }
  | { kind: "loaded"; personas: PersonaDto[] }
  | { kind: "error" };

/** The F35.2 "no active persona" sentinel — same value the Personas page's own
 * activate/deactivate control writes to this key. */
const NONE_VALUE = "0";
const NONE_LABEL = "None — persona-less patter";

function isPersonaList(raw: unknown): raw is PersonaDto[] {
  return (
    Array.isArray(raw) &&
    raw.every((entry) => {
      if (typeof entry !== "object" || entry === null) return false;
      const obj = entry as Record<string, unknown>;
      return typeof obj["id"] === "number" && typeof obj["name"] === "string";
    })
  );
}

/** Matches SettingField's shipped single-line control styling (text/number inputs). */
const CONTROL_CLASSES =
  "h-9 w-full max-w-md rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

/**
 * `Station:Persona:ActiveId`'s settings-page control (SPEC F54.3; registered in `SettingsForm`'s
 * per-key control-override registry, F54.1). Renders persona **names** fed by `GET /api/personas`
 * but submits the picked persona's id as the staged value (the wire contract this key already
 * has — the Personas page's own Activate/Deactivate button writes the same key with the same
 * id-as-string shape, F35.2). The first option is always "None — persona-less patter" (value
 * `"0"`). On fetch failure the control degrades to the shipped number input, so the field stays
 * writable by raw id. One fetch per mount, no polling/retry.
 */
export function PersonaSettingControl({
  controlId,
  value,
  onChange,
  disabled,
}: SettingControlProps): ReactNode {
  const [status, setStatus] = useState<PersonaListStatus>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;

    async function loadPersonas(): Promise<void> {
      try {
        const resp = await fetch("/api/personas");
        if (!resp.ok) {
          if (!cancelled) setStatus({ kind: "error" });
          return;
        }
        const raw = (await resp.json()) as unknown;
        if (!isPersonaList(raw)) {
          if (!cancelled) setStatus({ kind: "error" });
          return;
        }
        if (!cancelled) setStatus({ kind: "loaded", personas: raw });
      } catch {
        if (!cancelled) setStatus({ kind: "error" });
      }
    }

    void loadPersonas();
    return () => {
      cancelled = true;
    };
  }, []);

  if (status.kind === "error") {
    const noticeId = `${controlId}-notice`;
    return (
      <div className="flex flex-col gap-1.5">
        <input
          id={controlId}
          type="number"
          value={value}
          onChange={(e: ChangeEvent<HTMLInputElement>) => onChange(e.currentTarget.value)}
          disabled={disabled}
          aria-describedby={noticeId}
          className={`${CONTROL_CLASSES} max-w-xs tabular-nums`}
        />
        <p id={noticeId} className="text-[0.78rem] text-mute">
          Persona list unavailable — enter a persona id (0 = none).
        </p>
      </div>
    );
  }

  const isLoading = status.kind === "loading";
  const personas = status.kind === "loaded" ? status.personas : [];

  return (
    <select
      id={controlId}
      value={value}
      onChange={(e: ChangeEvent<HTMLSelectElement>) => onChange(e.currentTarget.value)}
      disabled={disabled || isLoading}
      className={CONTROL_CLASSES}
    >
      <option value={NONE_VALUE}>{NONE_LABEL}</option>
      {personas.map((persona) => (
        <option key={persona.id} value={String(persona.id)}>
          {persona.name}
        </option>
      ))}
    </select>
  );
}
