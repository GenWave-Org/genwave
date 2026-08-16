/** Wire shape of one face inside a `GET /api/fonts` pack row (SPEC F104.7, STORY-284, PLAN T203) —
 * see Host's `FontLibraryFaceDto`. `style` renders as plain text ONLY — see `FontLibraryPackDto`'s
 * own remarks below for the T199/T200 stored-family/style obligation this page closes. */
export interface FontLibraryFaceDto {
  file: string;
  style: string;
  byteSize: number;
}

/** Wire shape of one `GET /api/fonts` row (SPEC F104.7, STORY-284, PLAN T203) — an installed font
 * pack, metadata only (no face bytes on this wire). Mirrors Host's `FontLibraryPackDto`.
 * `license`/`sourceUrl`/`version`/`subset` are `null` only on the (should-never-happen) chance the
 * pack's stored `definition` manifest fails to re-parse server-side — `slug`/`family`/`faces`/
 * `importedFrom`/`importedAt` are unaffected either way, since none of them round-trips through
 * that parse.
 *
 * `family`/`FontLibraryFaceDto.style` are UNBOUNDED free-form prose (the T199/T200 stored-family/
 * style obligation) — this page renders both as plain text ONLY, never interpolated into a
 * stylesheet or inline `style` attribute (see `WardrobeClient`'s own remarks). */
export interface FontLibraryPackDto {
  slug: string;
  family: string;
  faces: FontLibraryFaceDto[];
  license: string | null;
  sourceUrl: string | null;
  version: string | null;
  subset: string | null;
  importedFrom: string;
  importedAt: string;
}

/**
 * One installed entry on a non-font wardrobe tab (gh-#393) — the kind-neutral projection the
 * Personas/Themes/Shows tabs share: a display name, an optional secondary line (a show's tagline;
 * personas/themes have none), and the db/25 provenance pair. Projected server-side in
 * `wardrobe/page.tsx` from each kind's own listing (`GET /api/personas` / `Station:Theme` choices
 * off `GET /api/settings` / `GET /api/shows`), admitting only genuinely-imported rows
 * (`importedFrom != null` — the same two-provenance-class rule `persona-catalog/page.tsx`'s own
 * fetchers follow): the Wardrobe lists what came off the catalog shelf, never what was authored in
 * place (authored personas/shows have their own pages).
 *
 * `name`/`detail` are free-form prose — rendered as plain React text nodes ONLY, the same
 * obligation `FontLibraryPackDto`'s own remarks pin for family/style.
 */
export interface InstalledEntryRow {
  slug: string;
  name: string;
  detail: string | null;
  importedFrom: string;
  importedAt: string;
}

/** One item on a `GET /api/avatar-packs` row (PLAN T294) — mirrors Host's
 * `AvatarPackSummaryItemDto`: a display name and an OPTIONAL "pairs well with" catalog persona slug
 * (SPEC F128.1), no bytes on this wire (the Avatars tab's own face grid reads bytes through the
 * TRANSIENT proxied catalog route instead, the F104 specimen precedent — mirrors
 * `AvatarDetailPanel`'s own pre-install face grid one directory up). `name` is UNBOUNDED free-form
 * prose (PLAN T294 rider 2 — see `../persona-catalog/avatar-format.ts`'s own remarks); this page
 * renders it through that shared clamp, never verbatim. */
export interface AvatarPackSummaryItemDto {
  name: string;
  suggestedPersona: string | null;
}

/** One `GET /api/avatar-packs` row (SPEC F128.3, PLAN T294) — an installed avatar pack, metadata
 * only (no item bytes on this wire). Mirrors Host's `AvatarPackSummaryDto`. `name` is the pack's own
 * manifest `packName`, re-parsed from the stored `definition` server-side — `null` only on the
 * (should-never-happen) chance that re-parse fails, degrading the SAME way `FontLibraryPackDto`'s
 * own `license`/`sourceUrl`/`version`/`subset` fields do; `slug`/`items`/`importedFrom`/`importedAt`
 * are unaffected either way, since none of them round-trips through that parse. `name`, like every
 * item's own `name` above, is UNBOUNDED free-form prose (PLAN T294 rider 2) — never rendered
 * verbatim, always through `../persona-catalog/avatar-format.ts`'s shared clamp. */
export interface AvatarPackSummaryDto {
  slug: string;
  name: string | null;
  items: AvatarPackSummaryItemDto[];
  importedFrom: string;
  importedAt: string;
}

/**
 * One `GET /api/icon-packs` row (SPEC F130.4/F130.5, STORY-337, PLAN T303/T304) — an installed icon
 * pack. Mirrors Host's `IconPackSummaryDto`. NO `name` field (unlike `AvatarPackSummaryDto.name`):
 * SPEC F130.1's own `gw-icon-pack` document has no pack-level display-name field at all — `slug` IS
 * the only honest label this schema can offer (`StationSettingsAllowlist.IconPackChoices`'s own
 * remarks give the same reasoning for a `Station:IconPack` choice's own label).
 *
 * `definition` DOES ride this listing wire (PLAN T304), unlike `AvatarPackSummaryDto`/
 * `FontLibraryPackDto`'s own "no bytes on the listing" posture: an icon pack carries no binary
 * assets at all (SPEC F130.6), only its own already-canonical, whitelist-safe JSON text — this is
 * what lets `IconWardrobeClient` draw a real specimen row per pack through the SAME safe renderer
 * `IconDetailPanel`/`Icon` use, parsed defensively client-side (`lib/icon-pack.ts`), never trusted
 * blindly even though this station's own install route already validated it once.
 */
export interface IconPackSummaryDto {
  slug: string;
  iconCount: number;
  definition: string;
  importedFrom: string;
  importedAt: string;
}
