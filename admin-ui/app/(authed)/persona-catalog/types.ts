/** Lowercase wire vocabulary the api mirrors verbatim from genwave-catalog's own schema (SPEC
 * F90.2) — never the C# enum's default PascalCase serialization. */
export type CatalogAudience = "everyone" | "mature";

/** The F103.1 entry-kind discriminator, lowercase — see Host's `CatalogEntryKind`/
 * `CatalogController.ToWireKind`. Always present on a shelf row: the api never omits `kind`, even
 * for a legacy persona entry whose own index.json predates the field (the api resolves that
 * default server-side). Widened to `"font"` at F104.1/T193, `"show"` at F118.1/T254, and to
 * `"avatar"`/`"icon"` at F128/T292 (the wire has emitted both since T292; this union catches up at
 * T294's own rider 1) — `"avatar"` gets a shelf card + detail routing NOW (this task); `"icon"`
 * stays routed to nothing (the shelf's own exhaustive-switch `default` arms already degrade an
 * unrecognised kind to "renders nothing" rather than misrouting it as a persona card — see
 * `PersonaCatalogClient.renderShelfEntry`'s own remarks) until T304 gives it a tab and a card of its
 * own to replace this "still-hidden" placeholder with. */
export type CatalogEntryKind = "persona" | "theme" | "font" | "show" | "avatar" | "icon";

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
 * `true`. `fontFamily`/`fontByteTotal`/`fontSpecimenFile`/`fontLicense`/`fontVersion`/`fontSubset`
 * are `null` for every non-font entry (T202, T204) — `fontFamily` here is parsed straight from
 * `card` (the manifest, T194), a DETAIL-side sibling of `CatalogShelfEntryDto.fontFamily`'s own
 * index-sourced field of the same name, not the same fetch; see `FontDetailPanel`'s own remarks for
 * why this value is never interpolated into CSS. `fontSpecimenFile` is the bare filename
 * `SpecimenBlock` passes to `GET /api/catalog/entries/{slug}/assets/{file}` to render the real face
 * (SPEC F104.4). `fontLicense`/`fontVersion`/`fontSubset` (PLAN T204, Dean's post-v3.1.0 review: the
 * pre-install review panel showed no licence anywhere) are the SAME manifest trio a Wardrobe pack's
 * own `license`/`version`/`subset` carry once installed (`FontLibraryPackDto`) — see
 * `font-format.ts`'s shared `licenceLine` for the one place both render identically.
 * `suggestedPersona` (SPEC F118.3, PLAN T254/T255) is a show entry's OPTIONAL "also hire" catalog
 * persona slug, parsed off the entry's `meta.json` server-side — `null` for every non-show entry,
 * when unreachable, when the entry's meta.json omits it, or when it fails its own slug-shape check
 * (see `CatalogController.ValidateSuggestedPersonaShape`). SOFT by design: `ShowCardReviewModal`
 * only ever surfaces this value to offer against — it is never itself validated here for "is this
 * slug actually on the shelf" or "is it already hired"; that eligibility check is `PersonaCatalogClient`'s
 * own job (SPEC F118.3's "on-shelf and un-hired" gate), reading this station's already-fetched
 * index/personas state, not a further catalog fetch.
 * `avatarItems` (SPEC F128.1, F128.4, PLAN T292/T294) is an avatar pack entry's own faces, parsed off
 * `card` (the pack's `.avatar.json` manifest) — `null` for every non-avatar entry, when unreachable,
 * or when the manifest fails to parse. See `CatalogAvatarItemDto`'s own remarks.
 * `personaAvatarFile` (SPEC F128.2, F128.7, PLAN T292/T297) is a PERSONA entry's OWN optional
 * sidecar face — the bare filename of its one avatar asset, ready to pass straight to
 * `GET /api/catalog/entries/{slug}/assets/{file}` the same way `fontSpecimenFile` already does for a
 * font pack's specimen face. `null` for every non-persona entry, when unreachable, or when this
 * persona entry declares no face — see Host's `CatalogEntryResponse.PersonaAvatarFile`.
 * `packName` (PLAN T304 rider 4) is an AVATAR entry's own manifest display name, parsed off `card`
 * at zero extra cost — `null` for every non-avatar entry, when unreachable, or on the (should-never-
 * happen) chance the manifest fails to parse; see `AvatarDetailPanel`'s own remarks for where this
 * closes the T294 "no pack-name field on the wire" stated deviation.
 * `iconCount` (PLAN T304 rider 4) is an ICON entry's own declared icon count, re-validated off
 * `card` at zero extra cost — `null` for every non-icon entry, when unreachable, or when the
 * manifest fails the whitelist gate (the safe renderer, `IconDetailPanel`, still draws whatever it
 * defensively can from `card` regardless of whether this count resolved). An icon pack carries no
 * `packName` at all (SPEC F130.1 — no pack-level display-name field exists), the reason these two
 * fields are separate rather than one shared slot. */
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
  fontLicense: string | null;
  fontVersion: string | null;
  fontSubset: string | null;
  suggestedPersona: string | null;
  avatarItems: CatalogAvatarItemDto[] | null;
  personaAvatarFile: string | null;
  packName: string | null;
  iconCount: number | null;
}

/**
 * One avatar pack item on the entry detail wire (SPEC F128.1, F128.4, PLAN T292/T294) — mirrors
 * Host's `CatalogAvatarItemDto`, itself the wire projection of one hash-verified `.avatar.json`
 * manifest item. `file` is the bare filename `GET /api/catalog/entries/{slug}/assets/{file}` already
 * serves, or `null` when the manifest names a file the index's own hash-verified `assets[]` never
 * actually declared — the SAME "never trust a manifest-only filename alone" posture
 * `CatalogEntryDetailDto.fontSpecimenFile` already carries for a font pack's own upright face; a
 * `null` file renders no image at all in the face grid (T294 rider), never an attempted fetch
 * against an unverified name. `suggestedPersona` is an OPTIONAL "pairs well with" catalog persona
 * slug (SPEC F128.1), shape-checked server-side the same way a show entry's own `suggestedPersona`
 * is — an OFFER only, this detail panel never auto-applies it.
 */
export interface CatalogAvatarItemDto {
  name: string;
  file: string | null;
  suggestedPersona: string | null;
}

/**
 * One catalog-imported theme's provenance (gh-#375, Dean's demo feedback — the theme half
 * of the v3.1.1 polish, mirroring the font half's `installedFontSlugs` — see
 * `PersonaCatalogClient`'s own remarks). Sourced from `GET /api/settings`'s own `Station:Theme`
 * choices (SPEC F103.11, PLAN T187's `SettingChoice.importedFrom`/`importedAt`), never a new
 * backend route: `persona-catalog/page.tsx` extracts every choice carrying provenance and hands the
 * list straight through — the smaller diff over adding a dedicated `GET /api/themes` listing (this
 * task's own dispatch weighed both; see that file's own remarks for the full reasoning).
 *
 * `slug` is the catalog entry's own slug, kept as its own field distinct from `importedFrom` even
 * though the two are always equal today (`ThemeInstallModal` always installs a theme under its own
 * catalog slug, threading that SAME value as both the import route's target slug and its
 * `?catalogSlug=`) — a caller keyed on `slug` never has to assume that equality holds, the same
 * "read the real field, don't infer it" discipline `WardrobeClient`'s own `ProvenanceChip` remarks
 * state for its own always-equal `importedFrom`/`slug` pair.
 */
export interface ThemeCatalogProvenanceDto {
  slug: string;
  importedFrom: string;
  importedAt: string;
}
