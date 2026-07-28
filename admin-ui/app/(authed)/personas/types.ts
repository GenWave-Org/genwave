/** Wire shape of one row from `GET/POST/PATCH /api/personas` (SPEC F35.4, F90.7, F94.2). `voice: ""`
 * is the station-default sentinel — the same convention `Station:Voice`/the F27 safe-segment
 * `voice` field already use. `slug` is the server's own `station.persona.slug` (PLAN T128 review
 * fix) — the ONLY value that may build a `GET/POST /api/personas/{slug}/export|import` link for
 * this row; it can diverge from a fresh client-side slugify of `name` (an imported persona keeps
 * whatever slug the import route was given, until the next admin edit), so `personaSlug(name)`
 * must never stand in for it. `importedFrom`/`importedAt` are the provenance stamp (T105 badge):
 * both `null` for an authored-in-place persona, both set — `importedFrom` to `"file"` or a catalog
 * slug, `importedAt` to an ISO timestamp — for an imported one. Required (not optional) keys:
 * the backend always serializes them, `null` or not (`PersonaDto.cs`), so a missing key here would
 * be a wire-shape bug, not an absent-value one. */
export interface PersonaDto {
  id: number;
  name: string;
  backstory: string;
  style: string;
  voice: string;
  slug: string;
  importedFrom: string | null;
  importedAt: string | null;
}
