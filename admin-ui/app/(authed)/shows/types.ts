/** Wire shape of a `GET/POST/PATCH /api/shows` row (SPEC F115.1, F115.3, F115.4; STORY-305/312) —
 * mirrors `GenWave.Host.Api.ShowDto` field for field. `flavor` is prompt-only and private
 * everywhere ELSE (SPEC F115.3, the persona-soul precedent) but present here on purpose: this IS
 * the admin surface that authors it. `importedFrom`/`importedAt` are the db/25 provenance pair
 * (SPEC F90.7) this page's own provenance line reads — both `null` for a show authored in place. */
export interface ShowDto {
  id: number;
  name: string;
  slug: string;
  tagline: string | null;
  flavor: string | null;
  importedFrom: string | null;
  importedAt: string | null;
}

/** One row of a successful `DELETE /api/shows/{slug}`'s 200 body (SPEC F115.4) — a show-scoped
 * imaging row (F117.1) this delete unscoped as a side effect, not blocked it. Mirrors
 * `GenWave.Host.Api.ScopedImagingRowDto` field for field; `title` is `null` only on the
 * (should-never-happen) chance `library.media` carries no title for that row. */
export interface ScopedImagingRowDto {
  mediaId: number;
  title: string | null;
}

/** Body of a successful `DELETE /api/shows/{slug}` that unscoped one or more imaging rows (SPEC
 * F115.4) — present only on that 200 path; a plain 204 means nothing was unscoped. Mirrors
 * `GenWave.Host.Api.ShowDeleteResponse`. */
export interface ShowDeleteResponseDto {
  unscopedImaging: ScopedImagingRowDto[];
}
