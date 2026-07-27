/** Wire shape of one row from `GET/POST/PATCH /api/personas` (SPEC F35.4, F90.7). `voice: ""` is
 * the station-default sentinel — the same convention `Station:Voice`/the F27 safe-segment `voice`
 * field already use. `importedFrom`/`importedAt` are the provenance stamp (T105 badge): both
 * `null` for an authored-in-place persona, both set — `importedFrom` to `"file"` or a catalog
 * slug, `importedAt` to an ISO timestamp — for an imported one. Required (not optional) keys:
 * the backend always serializes them, `null` or not (`PersonaDto.cs`), so a missing key here would
 * be a wire-shape bug, not an absent-value one. */
export interface PersonaDto {
  id: number;
  name: string;
  backstory: string;
  style: string;
  voice: string;
  importedFrom: string | null;
  importedAt: string | null;
}
