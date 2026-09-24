import type { SettingDto } from "./settings-types";

/**
 * Display grouping for the settings page (SPEC F205.5, STORY-478, PLAN T577). Presentation-only —
 * PUT semantics and key names are unchanged; this module only decides which card, in which order,
 * a key's field renders under. Sections come straight off the descriptor's own
 * {@link SettingDto.group} (PLAN T574/T576) — never derived from the key — so a key's section is
 * whatever the server assigned it, and this file carries no key-to-section mapping of its own.
 *
 * `GROUP_ORDER` mirrors `GenWave.Host.Configuration.SettingGroup`'s member order one-for-one (the
 * lowercase enum names `SettingDto.group.id` already carries) — a one-line connascence with that
 * C# enum, kept in sync by hand; no automated parity check pins the two together.
 */
export const GROUP_ORDER: readonly string[] = [
  "sound",
  "voice",
  "announcements",
  "sponsors",
  "library",
  "community",
  "station",
  "system",
];

export interface SettingsSection {
  id: string;
  label: string;
  settings: SettingDto[];
}

/**
 * Groups settings into display sections by `setting.group.id`, ordered per `GROUP_ORDER`, with
 * each key keeping its server-supplied relative order within its section. A group id
 * `GROUP_ORDER` doesn't know about — a future server addition this client hasn't caught up to —
 * renders AFTER every known section, in first-seen order, rather than being dropped: the same
 * fail-open posture the old prefix-based "other" fallback had.
 */
export function groupSettingsBySection(settings: SettingDto[]): SettingsSection[] {
  const bySection = new Map<string, SettingsSection>();
  for (const setting of settings) {
    const id = setting.group.id;
    const existing = bySection.get(id);
    if (existing) {
      existing.settings.push(setting);
    } else {
      bySection.set(id, { id, label: setting.group.label, settings: [setting] });
    }
  }

  const known: SettingsSection[] = [];
  for (const id of GROUP_ORDER) {
    const section = bySection.get(id);
    if (section) known.push(section);
  }

  const knownIds = new Set(GROUP_ORDER);
  const unknown = [...bySection.values()].filter((section) => !knownIds.has(section.id));

  return [...known, ...unknown];
}

/**
 * Filters each section's settings to those whose `label` or `help` contains `query` as a
 * case-insensitive substring (SPEC F205.5, STORY-478 AC7) — never the key, never the value. A
 * blank/whitespace-only query is a no-op (every section renders unfiltered); a section left with
 * no matching settings is dropped entirely, taking its index entry with it.
 */
export function filterSectionsByQuery(
  sections: readonly SettingsSection[],
  query: string
): SettingsSection[] {
  const needle = query.trim().toLowerCase();
  if (needle === "") return [...sections];

  return sections
    .map((section) => ({
      ...section,
      settings: section.settings.filter(
        (setting) =>
          setting.label.toLowerCase().includes(needle) || setting.help.toLowerCase().includes(needle)
      ),
    }))
    .filter((section) => section.settings.length > 0);
}
