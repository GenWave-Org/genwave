/**
 * Prettifies a catalog slug for shelf display (PLAN T102, SPEC F90.4a) — e.g. "late-night-lena" ->
 * "Late Night Lena". The index carries no separate display name (only slug/audience/bestFor,
 * F90.2's own "metadata and file pointers only" contract) — the shelf derives one from the slug
 * alone, title-casing each hyphen-separated word. `.charAt(0)` (never `word[0]`) is deliberate:
 * `noUncheckedIndexedAccess` types `word[0]` as `string | undefined`, and `.charAt(0)` returns
 * `""` for an empty string instead, so the already-filtered non-empty-word invariant never needs
 * a null-forgiving assertion to satisfy the compiler.
 */
export function prettifySlug(slug: string): string {
  return slug
    .split("-")
    .filter((word) => word.length > 0)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(" ");
}
