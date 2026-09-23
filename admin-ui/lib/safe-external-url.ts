/**
 * Whether `value` is safe to point an `<a href>` at — an absolute `http:`/`https:` URL, the one
 * shape this codebase trusts as a link target for author-declared, untrusted external data (a
 * jingle/font/voice pack manifest's own `sourceUrl`, an attribution line's own `sourceUrl`).
 * Anything else — `javascript:`, a bare string that isn't a URL at all, a relative path — must
 * render as plain text instead (security-web: never trust external input into an href unchecked).
 *
 * Shared by `JinglePackDetailPanel` (a pack manifest's per-asset credit link) and `AboutView` (an
 * attribution line's own credit link) — one guard, not two copies.
 */
export function isHttpUrl(value: string): boolean {
  return /^https?:/i.test(value);
}
