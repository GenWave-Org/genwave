import type { VoiceEngineEntry } from "./use-voice-engines";

/** The engine a bare voice id belongs to, and the key the DJ card's own voice rides under (SPEC F219.7). */
export const KOKORO_ENGINE = "kokoro";

/** A voice choice per engine: `{engine: voiceId}`. A missing engine means "no choice". */
export type EngineVoiceMap = Readonly<Record<string, string>>;

/**
 * Reads a voice setting value (`Station:Voice`, `Station:Ads:AnnouncerVoice`): a bare id is a Kokoro voice, a JSON
 * object is a per-engine map (SPEC F219.8), blank is no choice. Anything else (malformed JSON, non-string ids) reads
 * as no choice rather than throwing.
 */
export function parseVoiceSettingValue(value: string): EngineVoiceMap {
  const trimmed = value.trim();
  if (trimmed === "") return {};
  if (!trimmed.startsWith("{")) return { [KOKORO_ENGINE]: trimmed };
  let parsed: unknown;
  try {
    parsed = JSON.parse(trimmed);
  } catch {
    return {};
  }
  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) return {};
  const map: Record<string, string> = {};
  for (const [engine, voice] of Object.entries(parsed)) {
    if (typeof voice === "string" && voice.trim() !== "") map[engine] = voice;
  }
  return map;
}

/** Writes a voice setting value: nothing chosen is blank, Kokoro alone stays a bare id, anything else a JSON map. */
export function serializeVoiceSettingValue(map: EngineVoiceMap): string {
  const entries = Object.entries(map).filter(([, voice]) => voice.trim() !== "");
  if (entries.length === 0) return "";
  const [first] = entries;
  if (entries.length === 1 && first !== undefined && first[0] === KOKORO_ENGINE) return first[1];
  return JSON.stringify(Object.fromEntries(entries));
}

/** `map` with one engine's voice set, or removed when `voiceId` is blank. */
export function withEngineVoice(map: EngineVoiceMap, engine: string, voiceId: string): EngineVoiceMap {
  const next: Record<string, string> = { ...map };
  if (voiceId === "") delete next[engine];
  else next[engine] = voiceId;
  return next;
}

/** The registry engines with the active one first, the rest in their given order. */
export function activeFirst(engines: readonly VoiceEngineEntry[], active: string): VoiceEngineEntry[] {
  return [...engines.filter((e) => e.name === active), ...engines.filter((e) => e.name !== active)];
}

/** A DJ's voices for the form: its map without blank ids, plus the flat voice under its own engine if the map lacks it. */
export function personaVoiceMap(voice: string, ownEngine: string, voices: EngineVoiceMap | undefined): EngineVoiceMap {
  const map: Record<string, string> = {};
  for (const [engine, id] of Object.entries(voices ?? {})) if (id.trim() !== "") map[engine] = id;
  if (voice !== "" && map[ownEngine] === undefined) map[ownEngine] = voice;
  return map;
}

/** The most speakers `Station:Ads:CastVoices` takes (VoiceMap.CastMaxCount). */
export const CAST_MAX_COUNT = 16;

/**
 * Reads `Station:Ads:CastVoices` (F219.8): the old comma list of Kokoro ids, or a JSON array whose entries are each a
 * bare id (Kokoro) or an engine map. Anything unreadable is an empty cast.
 */
export function parseCastSettingValue(value: string): EngineVoiceMap[] {
  const trimmed = value.trim();
  if (trimmed === "") return [];
  if (!trimmed.startsWith("[")) {
    return trimmed
      .split(",")
      .map((id) => id.trim())
      .filter((id) => id !== "")
      .map((id) => ({ [KOKORO_ENGINE]: id }));
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(trimmed);
  } catch {
    return [];
  }
  if (!Array.isArray(parsed)) return [];
  return parsed.map((entry: unknown) => {
    if (typeof entry === "string") return entry.trim() === "" ? {} : { [KOKORO_ENGINE]: entry };
    return typeof entry === "object" && entry !== null ? parseVoiceSettingValue(JSON.stringify(entry)) : {};
  });
}

/** Writes `Station:Ads:CastVoices`: no speaker with a voice is blank, else a JSON array (a bare id for Kokoro alone). */
export function serializeCastSettingValue(cast: readonly EngineVoiceMap[]): string {
  const entries = cast
    .map((speaker) => serializeVoiceSettingValue(speaker))
    .filter((entry) => entry !== "")
    .map((entry): unknown => (entry.startsWith("{") ? JSON.parse(entry) : entry));
  return entries.length === 0 ? "" : JSON.stringify(entries);
}
