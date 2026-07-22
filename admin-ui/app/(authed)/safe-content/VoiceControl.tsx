"use client";

import { type ChangeEvent, type ReactNode } from "react";
import { useVoiceList } from "@/lib/use-voice-list";

interface VoiceControlProps {
  /** Current field value — "" means "Station default" (no `voice` field on the wire, F27.3). */
  value: string;
  onChange: (value: string) => void;
  disabled: boolean;
  /** DOM id for the label/control pair. Defaults to "safe-voice" (the original Safe Content
   * shape, unchanged for that page) — a caller mounting this control elsewhere (PersonasClient,
   * SPEC F79.5) passes its own id so an import-warning link can target this exact field via
   * `#id` without depending on a sibling feature's internal id string. */
  id?: string;
}

/** The select value that maps to "no voice field in the POST body" (unchanged wire contract). */
const STATION_DEFAULT_VALUE = "";

const FIELD_LABEL_CLASSES = "text-[0.82rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES =
  "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

/**
 * Voice control for the Generate form (SPEC F29.5, STORY-098, closes gitea-#183's UI half), also
 * reused by the Personas form (PersonasClient, SPEC F79.5, STORY-210). Renders a select with
 * "Station default" first/selected (submits NO `voice` field — the shipped omit-if-blank wire
 * contract carried over unchanged from the pre-R3 free-text input) plus one option per voice id
 * from `useVoiceList()` (submits that id). On listing failure it falls back to the shipped
 * free-text input so the form stays submittable, with a visible inline notice explaining why.
 */
export function VoiceControl({ value, onChange, disabled, id = "safe-voice" }: VoiceControlProps): ReactNode {
  const status = useVoiceList();

  if (status.kind === "error") {
    const noticeId = `${id}-notice`;
    return (
      <div className="flex flex-col gap-1.5">
        <label htmlFor={id} className={FIELD_LABEL_CLASSES}>Voice</label>
        <input
          id={id}
          name="voice"
          type="text"
          placeholder="Station default"
          value={value}
          onChange={(e: ChangeEvent<HTMLInputElement>) => onChange(e.currentTarget.value)}
          disabled={disabled}
          aria-describedby={noticeId}
          className={FIELD_INPUT_CLASSES}
        />
        <p id={noticeId} className="text-[0.78rem] text-mute">
          Voice list unavailable — type a voice id or leave blank for the station default.
        </p>
      </div>
    );
  }

  const isLoading = status.kind === "loading";

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className={FIELD_LABEL_CLASSES}>Voice</label>
      <select
        id={id}
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
