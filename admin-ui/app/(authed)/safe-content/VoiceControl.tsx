"use client";

import { useEffect, useState, type ChangeEvent, type ReactNode } from "react";

interface VoiceControlProps {
  /** Current field value — "" means "Station default" (no `voice` field on the wire, F27.3). */
  value: string;
  onChange: (value: string) => void;
  disabled: boolean;
}

type VoiceListStatus =
  | { kind: "loading" }
  | { kind: "loaded"; voices: string[] }
  | { kind: "error" };

/** The select value that maps to "no voice field in the POST body" (unchanged wire contract). */
const STATION_DEFAULT_VALUE = "";

function isVoiceIdList(raw: unknown): raw is string[] {
  return Array.isArray(raw) && raw.every((entry) => typeof entry === "string");
}

const FIELD_LABEL_CLASSES = "text-[0.82rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES =
  "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

/**
 * Voice control for the Generate form (SPEC F29.5, STORY-098, closes gitea-#183's UI half). Fetches
 * GET /api/voices once on mount and renders a select with "Station default" first/selected
 * (submits NO `voice` field — the shipped omit-if-blank wire contract carried over unchanged
 * from the pre-R3 free-text input) plus one option per listed voice id (submits that id). On
 * listing failure (non-OK response or network error) it falls back to the shipped free-text
 * input so generation stays possible, with a visible inline notice explaining why. No
 * polling/retry — one fetch per mount.
 */
export function VoiceControl({ value, onChange, disabled }: VoiceControlProps): ReactNode {
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
    return (
      <div className="flex flex-col gap-1.5">
        <label htmlFor="safe-voice" className={FIELD_LABEL_CLASSES}>Voice</label>
        <input
          id="safe-voice"
          name="voice"
          type="text"
          placeholder="Station default"
          value={value}
          onChange={(e: ChangeEvent<HTMLInputElement>) => onChange(e.currentTarget.value)}
          disabled={disabled}
          aria-describedby="safe-voice-notice"
          className={FIELD_INPUT_CLASSES}
        />
        <p id="safe-voice-notice" className="text-[0.78rem] text-mute">
          Voice list unavailable — type a voice id or leave blank for the station default.
        </p>
      </div>
    );
  }

  const isLoading = status.kind === "loading";

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor="safe-voice" className={FIELD_LABEL_CLASSES}>Voice</label>
      <select
        id="safe-voice"
        name="voice"
        value={value}
        onChange={(e: ChangeEvent<HTMLSelectElement>) => onChange(e.currentTarget.value)}
        disabled={disabled || isLoading}
        className={`${FIELD_INPUT_CLASSES} w-fit`}
      >
        <option value={STATION_DEFAULT_VALUE}>Station default</option>
        {status.kind === "loaded" &&
          status.voices.map((voiceId) => (
            <option key={voiceId} value={voiceId}>
              {voiceId}
            </option>
          ))}
      </select>
    </div>
  );
}
