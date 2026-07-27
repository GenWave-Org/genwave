/**
 * Shared wire shape + display helper for `POST /api/personas/{slug}/import` (SPEC F79.3, F90.5;
 * `PersonaImportResponse`) — hoisted (review finding #7, PLAN T103) so the file-upload panel
 * (`personas/PersonaImportPanel.tsx`) and the catalog/file review modal
 * (`_components/PersonaCardReviewModal.tsx`) read the exact same response shape instead of two
 * hand-typed copies that could silently drift apart.
 */

/** Response shape of a successful `POST /api/personas/{slug}/import`. */
export interface PersonaImportSuccessBody {
  name: string;
  warnings: string[];
}

/** "" is the station-default sentinel used everywhere a voice/engine field can defer (SPEC
 * F35.1) — one shared implementation for every surface that displays a persona card's voice or
 * engine field (previously three separately hand-written copies across two files). */
export function describeStationDefault(value: string): string {
  return value === "" ? "Station default" : value;
}
