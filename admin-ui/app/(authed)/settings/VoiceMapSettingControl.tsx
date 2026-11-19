"use client";

import type { ChangeEvent, ReactNode } from "react";
import { EngineVoicePickers } from "@/components/EngineVoicePickers";
import { parseVoiceSettingValue, serializeVoiceSettingValue } from "@/lib/engine-voice-map";
import { useSharedVoiceEngines } from "@/lib/voice-data";
import { ChoiceSettingControl } from "./ChoiceSettingControl";
import type { SettingControlProps } from "./settings-types";

const TEXT_CLASSES =
  "h-9 w-full max-w-md rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

interface VoiceMapSettingControlProps extends SettingControlProps {
  /** Words for an engine with no voice chosen. */
  emptyLabel: string;
  /** True when the setting cannot be blank: the last voice chosen then has no blank option to go back to. */
  requireVoice: boolean;
}

/**
 * The control for the single-voice settings (`Station:Voice`, `Station:Ads:AnnouncerVoice`; SPEC F219.8, F219.10): one
 * voice picker per engine, saved as a bare id when only Kokoro has one (what the setting always held) and as a JSON
 * `{engine: id}` map otherwise. Until the engine list is known, or if it cannot be read, the setting keeps the control
 * it had before: its choice list when the server sends one, else a text box. `Station:Ads:CastVoices` (a list of
 * voices) has its own, CastVoicesSettingControl.
 */
function VoiceMapSettingControl(props: VoiceMapSettingControlProps): ReactNode {
  const engines = useSharedVoiceEngines();

  if (engines.kind === "loaded") {
    return (
      <EngineVoicePickers
        engines={engines.data}
        voices={parseVoiceSettingValue(props.value)}
        onChange={(voices) => props.onChange(serializeVoiceSettingValue(voices))}
        disabled={props.disabled}
        idPrefix={props.controlId}
        emptyLabel={props.emptyLabel}
        requireVoice={props.requireVoice}
      />
    );
  }

  if ((props.choices?.length ?? 0) > 0) return <ChoiceSettingControl {...props} />;

  return (
    <input
      id={props.controlId}
      type="text"
      value={props.value}
      onChange={(e: ChangeEvent<HTMLInputElement>) => props.onChange(e.currentTarget.value)}
      disabled={props.disabled}
      className={TEXT_CLASSES}
    />
  );
}

/** `Station:Voice` cannot be blank (the server rejects it), so its last voice stays chosen. */
export function StationVoiceSettingControl(props: SettingControlProps): ReactNode {
  return <VoiceMapSettingControl {...props} emptyLabel="No voice" requireVoice />;
}

/** `Station:Ads:AnnouncerVoice` may be blank, which the server reads as "use the station's own voice". */
export function AnnouncerVoiceSettingControl(props: SettingControlProps): ReactNode {
  return <VoiceMapSettingControl {...props} emptyLabel="Station voice" requireVoice={false} />;
}
