/**
 * The imaging editor's show-scope picker (SPEC F117.1, F119.4; STORY-313, PLAN T246) — a station-wide
 * (default) or per-show scope for an authored imaging segment, gated to the `station_id` kind only
 * (see `SafeContentClient`'s own remarks on that gating decision). Mirrors the schedule grid picker's
 * `ScheduleShowOptionDto` posture (PLAN T245): a narrow `{id, name}` projection of `GET /api/shows`'s
 * full `GenWave.Host.Api.ShowDto`, since this folder has no reason to hold `slug`/`flavor`/provenance.
 */
export interface ImagingShowOption {
  id: number;
  name: string;
}

/**
 * Resolves a stored `show_id` to its display name against the already-loaded roster — `"Station-wide"`
 * for `null`/`undefined` (F117.1's only pre-scope meaning, and the shape every non-`station_id` row
 * carries under this editor's own kind-gated picker), `"Unknown show"` for an id the roster doesn't
 * (or no longer) contain — mirrors `ScheduleShowPicker`'s own `currentShowName` fallback.
 */
export function showScopeLabel(
  showId: number | null | undefined,
  shows: ImagingShowOption[]
): string {
  if (showId === null || showId === undefined) return "Station-wide";
  return shows.find((show) => show.id === showId)?.name ?? "Unknown show";
}
