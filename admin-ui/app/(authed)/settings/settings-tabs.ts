import type { SettingDto } from "./settings-types";

/**
 * Per-area tab derivation for the settings page (gh-#144). Presentation-only, exactly like
 * `settings-sections.ts` one layer down — PUT semantics, key names, and the section grouping
 * are unchanged; this module only decides which TAB a key's existing section card renders
 * under. A tab's id doubles as its `?tab=` deep-link value.
 *
 * Derivation rule: a key's area is the prefix before its first `:` (`Tts:Endpoint` → `Tts`),
 * mirroring how `StationSettingsAllowlist` namespaces its keys. The colon-less engine env
 * knobs (`GW_XFADE_*`, `GW_SAFE_GAP_SECONDS`) fold into the Station tab rather than minting
 * a tab of their own: the section layer deliberately seats them beside their Station-side
 * siblings (GW_XFADE_* with Station:Cadence/Rotation in Playout, GW_SAFE_GAP_SECONDS with
 * Station:SafeScope in Safe — see settings-sections.ts), and splitting them out would tear
 * those sections in half and render duplicate section headings on two tabs.
 */
const STATION_PREFIX = "Station";

/**
 * Display labels for prefixes whose verbatim form reads wrong as a tab caption. Anything
 * absent renders its prefix as-is, so a future allowlist area gets a tab without touching
 * this file.
 */
const TAB_LABEL_OVERRIDES: Readonly<Record<string, string>> = {
  Tts: "TTS",
  Llm: "LLM",
  DependencyHealth: "Dependency Health",
};

/** The area prefix a key groups under — the part before the first `:`; colon-less keys → Station. */
export function tabPrefixForKey(key: string): string {
  const colonIndex = key.indexOf(":");
  return colonIndex === -1 ? STATION_PREFIX : key.slice(0, colonIndex);
}

/** A prefix's stable deep-link id (`?tab=<id>`) — lowercased so shared URLs read quietly. */
export function tabIdForPrefix(prefix: string): string {
  return prefix.toLowerCase();
}

export function tabLabelForPrefix(prefix: string): string {
  return TAB_LABEL_OVERRIDES[prefix] ?? prefix;
}

export interface SettingsAreaTab {
  /** Deep-link id — lowercased prefix, used in `?tab=` and the tab/panel element ids. */
  id: string;
  /** The verbatim key prefix this tab groups (`Station`, `Tts`, …). */
  prefix: string;
  label: string;
  /** The tab's settings in their original page order — section grouping happens per-tab downstream. */
  settings: SettingDto[];
}

/**
 * Groups settings into area tabs: Station pinned first (it is the station's own console and by
 * far the busiest area), every other present area alphabetical after it. Keys keep their
 * incoming relative order within a tab, so the existing per-section ordering inside each tab
 * is untouched. Areas with no keys are omitted — tabs are derived from data, never a fixed list,
 * so a future allowlist namespace surfaces as a new tab rather than being dropped.
 */
export function groupSettingsByTab(settings: SettingDto[]): SettingsAreaTab[] {
  const byPrefix = new Map<string, SettingDto[]>();
  for (const setting of settings) {
    const prefix = tabPrefixForKey(setting.key);
    const bucket = byPrefix.get(prefix);
    if (bucket) {
      bucket.push(setting);
    } else {
      byPrefix.set(prefix, [setting]);
    }
  }

  const prefixes = Array.from(byPrefix.keys()).sort((a, b) => {
    if (a === STATION_PREFIX) return -1;
    if (b === STATION_PREFIX) return 1;
    // Plain codepoint comparison on the lowercased prefixes — deterministic across runtimes,
    // deliberately not locale-collated (the gh-#168 Jest locale pin exists for a reason).
    const left = a.toLowerCase();
    const right = b.toLowerCase();
    if (left < right) return -1;
    if (left > right) return 1;
    return 0;
  });

  return prefixes.map((prefix) => ({
    id: tabIdForPrefix(prefix),
    prefix,
    label: tabLabelForPrefix(prefix),
    settings: byPrefix.get(prefix) ?? [],
  }));
}
