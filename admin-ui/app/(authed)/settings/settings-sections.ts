import type { SettingDto } from "./settings-types";

/**
 * Display grouping for the settings page (SPEC F28.12). Presentation-only —
 * PUT semantics and key names are unchanged; this module only decides which
 * card a key's field renders under.
 */
export type SectionId = "loudness" | "playout" | "station" | "scope" | "safe" | "library" | "other";

/** Fixed render order — sections with no matching fields are simply omitted. */
export const SECTION_ORDER: readonly SectionId[] = [
  "loudness",
  "playout",
  "station",
  "scope",
  "safe",
  "library",
  "other",
];

export const SECTION_LABELS: Readonly<Record<SectionId, string>> = {
  loudness: "Loudness",
  playout: "Playout",
  station: "Station",
  scope: "Scope",
  safe: "Safe",
  library: "Library",
  other: "Other",
};

/**
 * Maps a setting key to its section by prefix, mirroring the authoritative
 * key list in `GenWave.Host.Configuration.StationSettingsAllowlist`:
 *   - `Loudness:*`                                             → Loudness
 *   - `Station:Cadence:*`, `Station:Rotation:*`, `GW_XFADE_*`   → Playout
 *   - `Station:Name`, `Station:Voice`, `Station:Persona:ActiveId` → Station
 *   - `Station:SafeScope:*`, `GW_SAFE_GAP_SECONDS`              → Safe
 *   - `Station:Scope:*`                                         → Scope
 *   - `Library:*`                                               → Library
 * Anything else falls back to "other" so a future allowlist addition is
 * surfaced rather than silently dropped from the page.
 *
 * `station` is a minimal V7 addition (SPEC F44.1/F44.5) carrying only three exact keys below —
 * NOT a `Station:` prefix rule, which would also swallow Cadence/Rotation/Scope/SafeScope.
 * V8 (SPEC F44.8) extends this section with `Station:Persona:ActiveId` (moved from `other`) and
 * adds the `library` section for every `Library:*` key. The Tts and Llm namespaces (including the
 * three V8 additions — RenderBudgetSeconds, BlurbRetentionHours, MaxCopyChars) have no obvious
 * section of their own and fall through to `other`, unchanged from before V8.
 */
export function sectionForKey(key: string): SectionId {
  if (key.startsWith("Loudness:")) return "loudness";
  if (key === "Station:Name" || key === "Station:Voice" || key === "Station:Persona:ActiveId")
    return "station";
  // Station:Rotation:* (SPEC F41.6) joins Station:Cadence:*/GW_XFADE_* in Playout — its prefix
  // doesn't overlap Station:Scope:*/Station:SafeScope:*, so it carries none of that pair's
  // check-order pitfall (see the SafeScope-before-Scope note below).
  if (
    key.startsWith("Station:Cadence:") ||
    key.startsWith("Station:Rotation:") ||
    key.startsWith("GW_XFADE")
  )
    return "playout";
  // SafeScope is checked before the plainer Scope prefix so it doesn't fall
  // through to "scope" (Station:SafeScope:* would otherwise match Station:Scope's
  // shorter prefix if the check order were reversed).
  // GW_SAFE_GAP_SECONDS (F29.8, STORY-100) is the engine-side sibling knob to SafeScope,
  // so it renders in the same Safe group.
  if (key.startsWith("Station:SafeScope:") || key === "GW_SAFE_GAP_SECONDS") return "safe";
  if (key.startsWith("Station:Scope:")) return "scope";
  // Every Library:* key (ScanIntervalSeconds, EnrichmentConcurrency, the enrichment-mode
  // CueDetection/Energy pair) — SPEC F44.8, closes gitea-#197.
  if (key.startsWith("Library:")) return "library";
  return "other";
}

export interface SettingsSection {
  id: SectionId;
  label: string;
  settings: SettingDto[];
}

/**
 * Groups settings into display sections in `SECTION_ORDER`, preserving each
 * key's relative order within its section. Sections with no matching fields
 * are omitted rather than rendered empty.
 */
export function groupSettingsBySection(settings: SettingDto[]): SettingsSection[] {
  const bySection = new Map<SectionId, SettingDto[]>();
  for (const setting of settings) {
    const id = sectionForKey(setting.key);
    const bucket = bySection.get(id);
    if (bucket) {
      bucket.push(setting);
    } else {
      bySection.set(id, [setting]);
    }
  }

  return SECTION_ORDER.filter((id) => bySection.has(id)).map((id) => ({
    id,
    label: SECTION_LABELS[id],
    settings: bySection.get(id) ?? [],
  }));
}
