/** The three Enrichment 2.0 signal columns, hidden by default (SPEC F49.3 — the literal gitea-#190
 * "off by default, turned on when needed"). Every other Catalog column is not toggleable this
 * phase. */
export type OptionalCatalogColumn = "year" | "bpm" | "energy";

export const OPTIONAL_CATALOG_COLUMNS: readonly OptionalCatalogColumn[] = ["year", "bpm", "energy"];

export const CATALOG_COLUMN_VISIBILITY_STORAGE_KEY = "genwave.catalog.columns";

const COLUMN_LABELS: Record<OptionalCatalogColumn, string> = {
  year: "Year",
  bpm: "BPM",
  energy: "Energy",
};

export function columnLabel(column: OptionalCatalogColumn): string {
  return COLUMN_LABELS[column];
}

function isOptionalCatalogColumn(value: unknown): value is OptionalCatalogColumn {
  return typeof value === "string" && (OPTIONAL_CATALOG_COLUMNS as readonly string[]).includes(value);
}

/**
 * Validates a parsed `localStorage` payload into the subset of optional columns it names —
 * drops anything unrecognized (a foreign/corrupt value, or a column name retired in a future
 * release) instead of trusting it (SPEC F49.3, `usePersistedState`'s boundary-data contract).
 */
export function parseVisibleColumns(raw: unknown): OptionalCatalogColumn[] | null {
  if (!Array.isArray(raw)) return null;
  return raw.filter(isOptionalCatalogColumn);
}

const EM_DASH = "—";

/** Plain integer, em-dash for null (SPEC F49.2/F49.3) — never a zero for a missing year. */
export function formatYearCell(year: number | null): string {
  return year !== null ? String(year) : EM_DASH;
}

/** One decimal, em-dash for null (SPEC F49.2/F49.3). */
export function formatBpmCell(bpm: number | null): string {
  return bpm !== null ? bpm.toFixed(1) : EM_DASH;
}

/** Two decimals, 0–1 range, em-dash for null (SPEC F49.2/F49.3). */
export function formatEnergyCell(energy: number | null): string {
  return energy !== null ? energy.toFixed(2) : EM_DASH;
}
