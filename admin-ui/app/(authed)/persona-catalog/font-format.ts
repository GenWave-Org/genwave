/** Human-readable byte total for a font pack (SPEC F104.3, F104.4) — shared between the shelf
 * card's byte-total line (T201) and the detail panel's own (T202) so the two can never drift on
 * the same 1024-based KiB/MiB ladder `HealthView`'s own `formatBytes` uses elsewhere in the app
 * (not imported from there — that one lives beside an unrelated container-memory concern, and this
 * three-line formatter is more abstraction than one extra import earns, YAGNI). Font packs stay
 * small (T195's own ≤200 KiB pack ceiling), so this renders KiB for every real pack today; MiB
 * only ever fires for a future pack that outgrows that ceiling. */
export function formatFontByteTotal(bytes: number): string {
  const kib = 1024;
  const mib = kib * 1024;
  if (bytes >= mib) return `${(bytes / mib).toFixed(1)} MiB`;
  if (bytes >= kib) return `${Math.round(bytes / kib)} KiB`;
  return `${bytes} B`;
}
