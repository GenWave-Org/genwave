/**
 * Validates a ?return= redirect target. Must be a relative path on this
 * origin: starts with "/" but NOT with "//" or "/\" (which browsers treat
 * as protocol-relative or host-relative after normalization).
 *
 * Returns "/" on any invalid or missing input.
 */
export function safeReturnTo(raw: unknown): string {
  if (typeof raw !== "string") return "/";
  if (raw.length === 0 || raw === "/") return "/dashboard";
  // Block protocol-relative and host-relative paths
  if (!/^\/[^/\\]/.test(raw)) return "/";
  return raw;
}
