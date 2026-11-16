/** The `Memory:LifeExtraction:Enabled` station setting key (a `GET /api/settings` row). */
const LIFE_EXTRACTION_KEY = "Memory:LifeExtraction:Enabled";

/**
 * Whether picking up life details is explicitly switched off, from a `GET /api/settings` body.
 * `true` only when the row's value, trimmed and lowercased, is "false"; a missing row or a
 * malformed body is `false` ("not known to be off"), so the UI never claims it is off without proof.
 */
export function lifeExtractionOffFrom(settings: unknown): boolean {
  if (!Array.isArray(settings)) return false;
  const row = (settings as { key?: unknown; value?: unknown }[]).find(
    (r) => typeof r === "object" && r !== null && r.key === LIFE_EXTRACTION_KEY
  );
  return typeof row?.value === "string" && row.value.trim().toLowerCase() === "false";
}
