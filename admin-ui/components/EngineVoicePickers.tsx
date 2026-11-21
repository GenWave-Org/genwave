"use client";

import type { ChangeEvent, ReactNode } from "react";
import { Button } from "@/components/ui/button";
import {
  activeFirst,
  withEngineVoice,
  type EngineVoiceMap,
} from "@/lib/engine-voice-map";
import { readErrorMessage } from "@/lib/problem-details";
import { requestTtsPreview, useTtsPreviewPlayer } from "@/lib/use-tts-preview";
import { useEngineVoiceList } from "@/lib/voice-data";
import type { VoiceEngineEntry, VoiceEngines } from "@/lib/use-voice-engines";

/** The line "Hear it" renders. Plain words, no persona text: it only has to carry the voice. */
export const HEAR_IT_SAMPLE_LINE = "Hello, this is how I sound.";

const FIELD_LABEL_CLASSES = "text-[0.82rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES =
  "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

export interface EngineVoicePickersProps {
  /** The registry engines and which one is active (`GET /api/voices/engines`). */
  engines: VoiceEngines;
  /** The chosen voice per engine; an engine without an entry has no choice. */
  voices: EngineVoiceMap;
  onChange: (voices: EngineVoiceMap) => void;
  disabled: boolean;
  /** DOM id prefix; each picker's select is `${idPrefix}-${engine}`. */
  idPrefix: string;
  /** Text of the "no choice" option, e.g. "Station voice". */
  emptyLabel: string;
  /** DJ pickers: an engine left empty while another has a voice means the DJ says nothing there. */
  quietWhenEmpty?: boolean;
  /** The setting cannot be blank: the one voice that is chosen has no blank option to go back to. */
  requireVoice?: boolean;
  /** Voices other holders already use, per engine; hidden from that engine's list (the picker's own value stays). */
  takenVoices?: Readonly<Record<string, readonly string[]>>;
}

/**
 * One voice picker per voice engine, the active engine first and marked "In use" (SPEC F219.10, STORY-556 AC5), each
 * with its own "Hear it" (AC6). Shared by the DJ form and the voice settings: it only edits a `{engine: voiceId}` map;
 * how that map is saved is the caller's business.
 */
export function EngineVoicePickers({
  engines,
  voices,
  onChange,
  disabled,
  idPrefix,
  emptyLabel,
  quietWhenEmpty = false,
  requireVoice = false,
  takenVoices,
}: EngineVoicePickersProps): ReactNode {
  const anyVoiceChosen = Object.values(voices).some((voice) => voice !== "");

  return (
    <div className="flex flex-col gap-3">
      {activeFirst(engines.engines, engines.active).map((engine) => (
        <EngineVoicePicker
          key={engine.name}
          engine={engine}
          isActive={engine.name === engines.active}
          value={voices[engine.name] ?? ""}
          onChange={(voiceId) =>
            onChange(withEngineVoice(voices, engine.name, voiceId))
          }
          disabled={disabled}
          id={`${idPrefix}-${engine.name}`}
          emptyLabel={emptyLabel}
          takenVoices={takenVoices?.[engine.name] ?? []}
          canClear={
            !(requireVoice && !anyOtherVoiceChosen(voices, engine.name))
          }
          noteWhenEmpty={
            quietWhenEmpty ? (anyVoiceChosen ? "quiet" : "station") : null
          }
        />
      ))}
    </div>
  );
}

function anyOtherVoiceChosen(voices: EngineVoiceMap, engine: string): boolean {
  return Object.entries(voices).some(
    ([name, voice]) => name !== engine && voice !== "",
  );
}

/** Plain words for what a picker left empty means. `quiet`: other engines have a voice; `station`: none has. */
function emptyNoteFor(
  noteWhenEmpty: "quiet" | "station",
  engine: string,
  cannotPickVoice: boolean,
): string {
  if (cannotPickVoice)
    return `${engine} has one voice of its own, so this DJ speaks in that voice.`;
  return noteWhenEmpty === "quiet"
    ? `No voice — this DJ stays quiet on ${engine}.`
    : "No voice chosen, so the station voice is used.";
}

interface EngineVoicePickerProps {
  engine: VoiceEngineEntry;
  isActive: boolean;
  value: string;
  onChange: (voiceId: string) => void;
  disabled: boolean;
  id: string;
  emptyLabel: string;
  /** False when this picker holds the only chosen voice of a setting that cannot be blank. */
  canClear: boolean;
  /** Voices already held elsewhere on this engine; not offered (the current value always is). */
  takenVoices: readonly string[];
  /** What to explain when nothing is chosen here (see {@link emptyNoteFor}), or null for nothing. */
  noteWhenEmpty: "quiet" | "station" | null;
}

function EngineVoicePicker({
  engine,
  isActive,
  value,
  onChange,
  disabled,
  id,
  emptyLabel,
  canClear,
  takenVoices,
  noteWhenEmpty,
}: EngineVoicePickerProps): ReactNode {
  const list = useEngineVoiceList(engine.name);
  const player = useTtsPreviewPlayer();

  function hearIt(): void {
    void player.hear(() =>
      requestTtsPreview(
        { text: HEAR_IT_SAMPLE_LINE, voice: value, engine: engine.name },
        readErrorMessage,
      ),
    );
  }

  const voiceIds = list.kind === "loaded" ? list.voices : [];
  // An engine whose list loaded empty (Piper) cannot choose a voice per request; the station falls back to its own.
  const cannotPickVoice = list.kind === "loaded" && voiceIds.length === 0;
  const emptyNote =
    noteWhenEmpty !== null && value === ""
      ? emptyNoteFor(noteWhenEmpty, engine.name, cannotPickVoice)
      : null;
  const savedIsOffList = value !== "" && !voiceIds.includes(value);
  const offeredIds = voiceIds.filter(
    (voiceId) => voiceId === value || !takenVoices.includes(voiceId),
  );

  return (
    <div
      role="group"
      aria-label={`Voice on ${engine.name}`}
      className="flex flex-col gap-1.5"
    >
      <div className="flex items-center gap-2">
        <label htmlFor={id} className={FIELD_LABEL_CLASSES}>
          Voice on {engine.name}
        </label>
        {isActive && (
          <span className="rounded-[6px] border border-line bg-surface-2 px-1.5 text-[0.75rem] font-semibold text-ink">
            In use
          </span>
        )}
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <select
          id={id}
          value={value}
          onChange={(e: ChangeEvent<HTMLSelectElement>) =>
            onChange(e.currentTarget.value)
          }
          disabled={disabled || list.kind === "loading"}
          className={`${FIELD_INPUT_CLASSES} w-fit`}
        >
          {(canClear || value === "") && <option value="">{emptyLabel}</option>}
          {offeredIds.map((voiceId) => (
            <option key={voiceId} value={voiceId}>
              {voiceId}
            </option>
          ))}
          {savedIsOffList && <option value={value}>{value}</option>}
        </select>
        <Button
          type="button"
          variant="secondary"
          onClick={hearIt}
          disabled={disabled || player.isRendering || value === ""}
          aria-label={`Hear it on ${engine.name}`}
        >
          {player.isRendering ? "Rendering…" : "Hear it"}
        </Button>
        {player.url !== null && (
          <audio
            controls
            autoPlay
            src={player.url}
            aria-label={`Sample on ${engine.name}`}
          />
        )}
      </div>
      {list.kind === "error" && (
        <p className="text-[0.78rem] text-mute">
          Voice list unavailable for {engine.name}.
        </p>
      )}
      {emptyNote !== null && (
        <p className="text-[0.78rem] text-mute">{emptyNote}</p>
      )}
    </div>
  );
}
