"use client";

import { useEffect, useState, type ChangeEvent, type ReactNode } from "react";
import type { SettingControlProps } from "./settings-types";

type VoiceListStatus =
  | { kind: "loading" }
  | { kind: "loaded"; voices: string[] }
  | { kind: "error" };

function isVoiceIdList(raw: unknown): raw is string[] {
  return Array.isArray(raw) && raw.every((entry) => typeof entry === "string");
}

/** Matches SettingField's shipped single-line control styling (text/number inputs). */
const CONTROL_CLASSES =
  "h-9 w-full max-w-md rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

/**
 * `Station:Voice`'s settings-page control (SPEC F54.2; registered in `SettingsForm`'s per-key
 * control-override registry, F54.1). A sibling of the safe-content `VoiceControl`
 * (`../safe-content/VoiceControl.tsx`), not a reuse — that control's first/selected "Station
 * default" option encodes *its* field's blank-means-default wire contract, which `Station:Voice`
 * does not share (the validator requires non-blank, so an empty option would be a dead end).
 * Deltas from that precedent:
 *   - no empty option;
 *   - the current value preselected;
 *   - a current value missing from the fetched list is still offered, marked "(current)" —
 *     an external `Tts:Endpoint` may serve a voice set the shipped Kokoro list doesn't know
 *     about (F36), so silently dropping it would strand the operator on an unrelated voice.
 * Fetch failure degrades to the same free-text-input-plus-notice fallback as the safe-content
 * control. One fetch per mount, no polling/retry.
 */
export function VoiceSettingControl({
  controlId,
  value,
  onChange,
  disabled,
}: SettingControlProps): ReactNode {
  const [status, setStatus] = useState<VoiceListStatus>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;

    async function loadVoices(): Promise<void> {
      try {
        const resp = await fetch("/api/voices");
        if (!resp.ok) {
          if (!cancelled) setStatus({ kind: "error" });
          return;
        }
        const raw = (await resp.json()) as unknown;
        if (!isVoiceIdList(raw)) {
          if (!cancelled) setStatus({ kind: "error" });
          return;
        }
        if (!cancelled) setStatus({ kind: "loaded", voices: raw });
      } catch {
        if (!cancelled) setStatus({ kind: "error" });
      }
    }

    void loadVoices();
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
          type="text"
          value={value}
          onChange={(e: ChangeEvent<HTMLInputElement>) => onChange(e.currentTarget.value)}
          disabled={disabled}
          aria-describedby={noticeId}
          className={CONTROL_CLASSES}
        />
        <p id={noticeId} className="text-[0.78rem] text-mute">
          Voice list unavailable — type a voice id.
        </p>
      </div>
    );
  }

  const isLoading = status.kind === "loading";
  const knownVoices = status.kind === "loaded" ? status.voices : [];
  const currentIsOffList = value !== "" && !knownVoices.includes(value);

  return (
    <select
      id={controlId}
      value={value}
      onChange={(e: ChangeEvent<HTMLSelectElement>) => onChange(e.currentTarget.value)}
      disabled={disabled || isLoading}
      className={CONTROL_CLASSES}
    >
      {currentIsOffList && <option value={value}>{`${value} (current)`}</option>}
      {knownVoices.map((voiceId) => (
        <option key={voiceId} value={voiceId}>
          {voiceId}
        </option>
      ))}
    </select>
  );
}
