/**
 * Client-side, NON-authoritative read of a portable persona card (SPEC F79.1, F79.5, F79.6). Used
 * two ways: previewing an uploaded `<slug>.persona.json` file before import
 * (`PersonaImportPanel`), and reading back an existing persona's authored voice via
 * `GET /api/personas/{slug}/export` for the import-warning derivation
 * (`usePersonaVoiceWarning`) — the same download endpoint the Export button navigates to, just
 * consumed as JSON text instead of a browser download.
 *
 * The server (`PersonaCardSerializer.Deserialize` + `PersonaController.Import`) remains the only
 * validator: a card that fails to parse here still gets a real import attempt against the raw
 * bytes this module never touches. This only ever degrades to `null`, never throws.
 */

export interface PersonaCardPreview {
  name: string;
  tagline: string;
  /** "" when the card carries no voice (or an empty voiceId) — the same station-default
   * sentinel used everywhere else in this codebase (SPEC F35.1). */
  voiceId: string;
  quirkCount: number;
  loreCount: number;
  /** 0 when `taste` is omitted — every pre-F79 card, and any card authored without taste rules
   * (SPEC F79.2's additive, nullable-and-omitted-when-null `taste` field). */
  tasteCount: number;
}

interface RawVoiceSpec {
  voiceId?: unknown;
}

interface RawPersonaCard {
  name?: unknown;
  tagline?: unknown;
  voice?: RawVoiceSpec;
  quirks?: unknown;
  lore?: unknown;
  taste?: unknown;
}

function isRawPersonaCard(raw: unknown): raw is RawPersonaCard {
  return typeof raw === "object" && raw !== null;
}

/**
 * Parses `json` into a preview-only projection of a `PersonaCard` (SPEC F71.1's camelCase wire
 * shape). Returns `null` for anything that isn't valid JSON, isn't an object, or is missing a
 * usable `name` — every other field degrades to an empty/zero default rather than failing the
 * whole parse, since only `name` is load-bearing here (display, and slug derivation via
 * `personaSlug`).
 */
export function parsePersonaCardPreview(json: string): PersonaCardPreview | null {
  let raw: unknown;
  try {
    raw = JSON.parse(json);
  } catch {
    return null;
  }

  if (!isRawPersonaCard(raw) || typeof raw.name !== "string" || raw.name.trim() === "") {
    return null;
  }

  const voiceId = typeof raw.voice?.voiceId === "string" ? raw.voice.voiceId : "";

  return {
    name: raw.name,
    tagline: typeof raw.tagline === "string" ? raw.tagline : "",
    voiceId,
    quirkCount: Array.isArray(raw.quirks) ? raw.quirks.length : 0,
    loreCount: Array.isArray(raw.lore) ? raw.lore.length : 0,
    tasteCount: Array.isArray(raw.taste) ? raw.taste.length : 0,
  };
}
