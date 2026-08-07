/** Wire shape of one font asset inside a `GET /api/themes` theme's `fonts.display`/`fonts.sans`
 * (SPEC F104.11, PLAN T206) — see Host's `ThemeFontAsset`. Opaque to this page beyond `src`: the
 * editor never edits weight/style directly. An UNASSIGNED role's asset(s) pass through byte-untouched
 * (review finding F3 — including a multi-asset weight range or an italic sibling); only an EXPLICIT
 * assignment REPLACES the whole face with a single-asset, weight-400/style-normal shape — see
 * `EditorClient`'s own remarks. */
export interface ThemeFontAssetDto {
  src: string;
  weight: string;
  style: string;
}

/** Wire shape of one theme font role (`display` or `sans`) — see Host's `ThemeFontFace`. */
export interface ThemeFontFaceDto {
  family: string;
  assets: ThemeFontAssetDto[];
}

/** Wire shape of a theme's two required font roles — see Host's `ThemeFonts`. */
export interface ThemeFontsDto {
  display: ThemeFontFaceDto;
  sans: ThemeFontFaceDto;
}

/** Wire shape of a theme's light/dark token sets — see Host's `ThemeModes`. Opaque to this page:
 * the editor mixes fonts only (SPEC F104.11 "component mix only, no token-level colour editing"),
 * so these token maps only ever ride through unread, copied verbatim from the picked base theme
 * into the remix POSTed to `/api/themes/preview`. */
export interface ThemeModesDto {
  light: Record<string, string>;
  dark: Record<string, string>;
}

/** Wire shape of one `GET /api/themes` row (SPEC F104.11, STORY-286, PLAN T206) — a full,
 * resolvable theme manifest (shipped, imported, or saved — the base-theme picker's own candidate
 * list). Mirrors Host's `ThemeManifest` field-for-field: the manifest format IS the interchange
 * format (`ThemeManifestSerializer`'s own remarks) both `POST /api/themes/{slug}/import` and
 * `POST /api/themes/preview` already accept verbatim, so this type is a plain re-typing of that
 * same shape for THIS page's own read, not a narrower projection. */
export interface ThemeSummaryDto {
  slug: string;
  name: string;
  author: string;
  fonts: ThemeFontsDto;
  modes: ThemeModesDto;
}

/** Wire shape of one `GET /api/fonts/vendored` row (SPEC F104.11, STORY-286, PLAN T206; widened at
 * T206 review finding F4) — see Host's `VendoredFontDto`. This route now returns the editor's ENTIRE
 * assignable set, vendored ∪ installed, one row per family — `family` may be build-time vendored data
 * OR an installed pack's stored `FontPack.Family` (the SAME "unbounded, don't trust as CSS-safe on
 * its own" data class `FontLibraryPackDto.family` already carries); this type carries no promise
 * about which. `family` DOES eventually reach a real stylesheet if the option it labels is assigned
 * to a role in `EditorClient` (review finding F2, correcting this comment's former, incorrect "never
 * interpolated into a stylesheet" claim) — what makes that safe is server-side, not anything on this
 * side of the wire: assigning a face threads `family` into the remix POSTed to `POST
 * /api/themes/preview`, which parses it through `ThemeManifestParser.Parse`'s own `FontFamilyPattern`
 * re-check BEFORE `ThemeCssComposer` ever composes it, rejecting anything CSS-unsafe with a 400
 * regardless of where `family` originated. Here it is rendered as plain picker-option text only. */
export interface VendoredFontDto {
  family: string;
  src: string;
}
