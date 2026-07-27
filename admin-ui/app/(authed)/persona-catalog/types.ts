/** Lowercase wire vocabulary the api mirrors verbatim from genwave-catalog's own schema (SPEC
 * F90.2) — never the C# enum's default PascalCase serialization. */
export type CatalogAudience = "everyone" | "mature";

/** One `GET /api/catalog/index` row (SPEC F90.2, F90.4a) — see Host's `CatalogShelfEntryDto`.
 * Slug/audience/bestFor ONLY — tagline/description/author/sample patter live in the per-entry
 * `GET /api/catalog/entries/{slug}` fetch below, never eagerly loaded for the whole shelf. */
export interface CatalogShelfEntryDto {
  slug: string;
  audience: CatalogAudience;
  bestFor: string[];
}

/** Wire shape of `GET /api/catalog/index` (SPEC F90.2, F90.4) — see Host's `CatalogIndexResponse`.
 * `entries`/`fetchedAt` are `null` exactly when `unreachable` is `true`. */
export interface CatalogIndexResponseDto {
  entries: CatalogShelfEntryDto[] | null;
  fetchedAt: string | null;
  unreachable: boolean;
}

/** Wire shape of `GET /api/catalog/entries/{slug}` (SPEC F90.2, F90.3, F90.4a) — see Host's
 * `CatalogEntryResponse`. `card` carries the raw hash-verified JSON text — the detail panel itself
 * reads the already-projected fields below, but `card` is exactly what `PersonaCardReviewModal`
 * (SPEC F90.5/F90.6, PLAN T103) both renders in full and POSTs byte-for-byte on confirm; `meta`
 * stays unused by this page. Every field but `unreachable` is `null` exactly when `unreachable` is
 * `true`. */
export interface CatalogEntryDetailDto {
  card: string | null;
  meta: string | null;
  fetchedAt: string | null;
  unreachable: boolean;
  audience: CatalogAudience | null;
  bestFor: string[] | null;
  author: string | null;
  description: string | null;
  samplePatter: string[] | null;
}
