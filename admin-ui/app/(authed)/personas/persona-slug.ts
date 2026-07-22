/**
 * Mirrors the backend's `LegacyPersonaCardMapper.Slugify` (SPEC F71.1) exactly: lowercase, every
 * run of non-alphanumeric characters collapses to one hyphen, leading/trailing hyphens trimmed,
 * `"persona"` when that leaves nothing. A deliberate client-side duplicate of one small,
 * deterministic pure function — not a second source of truth.
 *
 * `GET/POST /api/personas/{slug}/export|import` (SPEC F79.1, F79.3; PLAN T66/T67, already merged)
 * address a persona by this slug, but `PersonaDto` carries no `slug` field (confirmed no new
 * schema needed for this task). Both the Export link and the Import target reproduce it from a
 * `name` the backend already ran the identical algorithm over: the persona's own name for export,
 * and the uploaded card's own `name` field for import — so a cross-station import lands on
 * exactly the slug the ORIGIN station assigned, without trusting the downloaded file's own name.
 */
export function personaSlug(name: string): string {
  const slug = name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return slug.length === 0 ? "persona" : slug;
}
