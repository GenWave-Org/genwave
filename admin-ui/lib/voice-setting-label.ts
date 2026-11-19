function parseEngineMap(value: string): Record<string, string> | null {
  if (!value.trimStart().startsWith("{")) return null;
  let parsed: unknown;
  try {
    parsed = JSON.parse(value);
  } catch {
    return null;
  }
  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) return null;
  const map: Record<string, string> = {};
  for (const [engine, voice] of Object.entries(parsed)) {
    if (typeof voice !== "string") return null;
    map[engine] = voice;
  }
  return map;
}

/** Plain case-insensitive ordinal order, matching the server's OrdinalIgnoreCase sort. */
function compareEngines(a: string, b: string): number {
  const left = a.toLowerCase();
  const right = b.toLowerCase();
  return left < right ? -1 : left > right ? 1 : 0;
}

/**
 * Plain words for a voice setting value (SPEC F219.8). A voice is either a bare id (read as a Kokoro voice) or a
 * JSON map of engine to voice id; the map reads as `One voice per engine (chatterbox: nova, kokoro: af_nova)`,
 * the same wording the settings list gets from the server, instead of raw JSON.
 */
export function voiceSettingLabel(value: string): string {
  const map = parseEngineMap(value);
  if (map === null) return value;
  const parts = Object.keys(map)
    .sort(compareEngines)
    .map((engine) => `${engine}: ${map[engine]}`);
  return `One voice per engine (${parts.join(", ")})`;
}
