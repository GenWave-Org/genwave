/** Lowercase wire vocabulary the api mirrors verbatim from genwave-catalog's own schema (SPEC
 * F90.2) — never the C# enum's default PascalCase serialization. */
export type CatalogAudience = "everyone" | "mature";

/** The F103.1 entry-kind discriminator, lowercase — see Host's `CatalogEntryKind`/
 * `CatalogController.ToWireKind`. Always present on a shelf row: the api never omits `kind`, even
 * for a legacy persona entry whose own index.json predates the field (the api resolves that
 * default server-side). Widened to `"font"` at F104.1/T193. */
export type CatalogEntryKind = "persona" | "theme" | "font";

/** One mode's five shelf-chip swatches (SPEC F103.4, PLAN T185) — see Host's
 * `CatalogShelfSwatchSetDto`. `"accent-2"` keeps its hyphenated wire name (the app's own
 * `ThemeModes` token vocabulary), not `accent2`. */
export interface CatalogThemeSwatchSet {
  bg: string;
  surface: string;
  ink: string;
  accent: string;
  "accent-2": string;
}

/** A theme entry's shelf-card preview (SPEC F103.4) — see Host's `CatalogShelfPreviewDto`. */
export interface CatalogThemePreview {
  light: CatalogThemeSwatchSet;
  dark: CatalogThemeSwatchSet;
}

/** One `GET /api/catalog/index` row (SPEC F90.2, F90.4a, F103.3, F103.4, F104.3) — see Host's
 * `CatalogShelfEntryDto`. `preview` is `null` for every persona entry, and for a theme entry whose
 * index carries none (an older index, T185) — tagline/description/author/sample patter still live
 * in the per-entry `GET /api/catalog/entries/{slug}` fetch below, never eagerly loaded for the
 * whole shelf. `fontFamily`/`fontByteTotal` are `null` for every persona/theme entry (T194, T201).
 * `fontFamily` here is the INDEX's own optional field — this one costs zero fetch (straight off the
 * index row). A manifest-sourced sibling of the same name arrives on the DETAIL wire
 * (`CatalogEntryDetailDto` below) at T202, costing the one fetch the detail panel already pays for.
 * A font entry's `description` rides that same per-entry detail fetch, not this shelf row — the
 * index carries no description field for any kind (STORY-281 AC1 reconciliation, T201). */
export interface CatalogShelfEntryDto {
  slug: string;
  kind: CatalogEntryKind;
  audience: CatalogAudience;
  bestFor: string[];
  preview: CatalogThemePreview | null;
  fontFamily: string | null;
  fontByteTotal: number | null;
}

/** Wire shape of `GET /api/catalog/index` (SPEC F90.2, F90.4) — see Host's `CatalogIndexResponse`.
 * `entries`/`fetchedAt` are `null` exactly when `unreachable` is `true`. */
export interface CatalogIndexResponseDto {
  entries: CatalogShelfEntryDto[] | null;
  fetchedAt: string | null;
  unreachable: boolean;
}

/** Wire shape of `GET /api/catalog/entries/{slug}` (SPEC F90.2, F90.3, F90.4a, F104.4) — see Host's
 * `CatalogEntryResponse`. `card` carries the raw hash-verified JSON text — the detail panel itself
 * reads the already-projected fields below, but `card` is exactly what `PersonaCardReviewModal`
 * (SPEC F90.5/F90.6, PLAN T103) both renders in full and POSTs byte-for-byte on confirm; `meta`
 * stays unused by this page. Every field but `unreachable` is `null` exactly when `unreachable` is
 * `true`. `fontFamily`/`fontByteTotal`/`fontSpecimenFile` are `null` for every non-font entry
 * (T202) — `fontFamily` here is parsed straight from `card` (the manifest, T194), a DETAIL-side
 * sibling of `CatalogShelfEntryDto.fontFamily`'s own index-sourced field of the same name, not the
 * same fetch; see `FontDetailPanel`'s own remarks for why this value is never interpolated into
 * CSS. `fontSpecimenFile` is the bare filename `SpecimenBlock` passes to
 * `GET /api/catalog/entries/{slug}/assets/{file}` to render the real face (SPEC F104.4). */
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
  fontFamily: string | null;
  fontByteTotal: number | null;
  fontSpecimenFile: string | null;
}
