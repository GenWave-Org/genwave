/**
 * Fields the reenrich pickers offer — CatalogToolbar's bulk fieldset and ReanalyzePanel's
 * single-row panel both build their checkbox list from this one module (SPEC F20.9–F20.11,
 * F46.4, F48.6) so the two pickers can't drift apart on which tokens exist or how they're
 * labeled.
 */
export type ReenrichField = "cue" | "energy" | "loudness" | "tags" | "bpm" | "year";

export const ALL_REENRICH_FIELDS: ReenrichField[] = ["cue", "energy", "loudness", "tags", "bpm", "year"];

/** Display label per field — cue/energy/loudness/tags keep the raw token as their accessible
 * name (unchanged since before F46.4/F48.6). bpm/year get an honest label instead of their raw
 * token: "year" nulls only `year_lookup_at` and retries the MusicBrainz lookup (F48.6), it does
 * not re-read tags, so it needs to read differently from the other four re-read-from-file
 * tokens. */
export const REENRICH_FIELD_LABELS: Record<ReenrichField, string> = {
  cue: "cue",
  energy: "energy",
  loudness: "loudness",
  tags: "tags",
  bpm: "BPM",
  year: "Year lookup (retry)",
};

/** Extra hint for the year token specifically — surfaced via `title` (a tooltip on hover/focus),
 * since the fieldset only has room for the short label above. */
export const REENRICH_FIELD_HINTS: Partial<Record<ReenrichField, string>> = {
  year: "Retries the MusicBrainz lookup for rows still missing a year — does not re-read tags.",
};
