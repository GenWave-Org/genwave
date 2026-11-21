"use client";

import { useState, type ChangeEvent, type ReactNode } from "react";
import { EngineVoicePickers } from "@/components/EngineVoicePickers";
import { Button } from "@/components/ui/button";
import {
  CAST_MAX_COUNT,
  parseCastSettingValue,
  serializeCastSettingValue,
  type EngineVoiceMap,
} from "@/lib/engine-voice-map";
import { useSharedVoiceEngines } from "@/lib/voice-data";
import type { SettingControlProps } from "./settings-types";

function hasVoice(speaker: EngineVoiceMap): boolean {
  return Object.values(speaker).some((voice) => voice !== "");
}

/** Per engine, the voices the speakers other than `index` hold. */
function voicesHeldByOthers(
  cast: readonly EngineVoiceMap[],
  index: number,
): Record<string, string[]> {
  const held: Record<string, string[]> = {};
  cast.forEach((speaker, i) => {
    if (i === index) return;
    for (const [engine, voice] of Object.entries(speaker)) {
      if (voice !== "") (held[engine] ??= []).push(voice);
    }
  });
  return held;
}

const TEXT_CLASSES =
  "h-9 w-full max-w-md rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

/**
 * The control for `Station:Ads:CastVoices` (SPEC F219.8, F219.10): a list of speakers, each with a voice picker per
 * engine, added and removed one at a time. Saved as a JSON array (a bare id for a speaker with Kokoro alone, an engine
 * map otherwise); the old comma list still reads. A speaker with no voice yet is kept on screen but not saved. The last
 * speaker holding a voice cannot be removed or cleared (the server rejects an empty cast), and a voice another speaker
 * holds on an engine is not offered there (the server rejects a shared voice). Until the engine list is known, or if it
 * cannot be read, the setting stays a text box.
 */
export function CastVoicesSettingControl(
  props: SettingControlProps,
): ReactNode {
  const engines = useSharedVoiceEngines();
  const { value, onChange } = props;
  const [cast, setCast] = useState<EngineVoiceMap[]>(() =>
    parseCastSettingValue(value),
  );

  // A value changed from outside (a save re-baselines it, a reset) replaces what is on screen. Our own edits already
  // match it, so a speaker added but not yet given a voice stays.
  const [seenValue, setSeenValue] = useState(value);
  if (seenValue !== value) {
    setSeenValue(value);
    if (serializeCastSettingValue(cast) !== value)
      setCast(parseCastSettingValue(value));
  }

  function update(next: EngineVoiceMap[]): void {
    setCast(next);
    onChange(serializeCastSettingValue(next));
  }

  if (engines.kind !== "loaded") {
    return (
      <input
        id={props.controlId}
        type="text"
        value={value}
        onChange={(e: ChangeEvent<HTMLInputElement>) =>
          onChange(e.currentTarget.value)
        }
        disabled={props.disabled}
        className={TEXT_CLASSES}
      />
    );
  }

  return (
    <div id={props.controlId} className="flex flex-col gap-3">
      {cast.map((speaker, index) => {
        // The one speaker holding the cast's only voice cannot lose it: the server rejects a cast with none.
        const isLastWithVoice = !cast.some(
          (other, i) => i !== index && hasVoice(other),
        );
        return (
          <div
            // Position is the identity: a speaker has no other key.
            key={index}
            role="group"
            aria-label={`Speaker ${index + 1}`}
            className="flex flex-col gap-2 rounded-[6px] border border-line p-3"
          >
            <EngineVoicePickers
              engines={engines.data}
              voices={speaker}
              onChange={(voices) =>
                update(cast.map((s, i) => (i === index ? voices : s)))
              }
              disabled={props.disabled}
              idPrefix={`${props.controlId}-${index}`}
              emptyLabel="None"
              requireVoice={isLastWithVoice}
              takenVoices={voicesHeldByOthers(cast, index)}
            />
            <Button
              type="button"
              variant="secondary"
              className="w-fit"
              onClick={() => update(cast.filter((_, i) => i !== index))}
              disabled={
                props.disabled ||
                cast.length <= 1 ||
                (isLastWithVoice && hasVoice(speaker))
              }
              aria-label={`Remove speaker ${index + 1}`}
            >
              Remove
            </Button>
          </div>
        );
      })}
      <Button
        type="button"
        variant="secondary"
        className="w-fit"
        onClick={() => setCast([...cast, {}])}
        disabled={props.disabled || cast.length >= CAST_MAX_COUNT}
      >
        Add a speaker
      </Button>
    </div>
  );
}
