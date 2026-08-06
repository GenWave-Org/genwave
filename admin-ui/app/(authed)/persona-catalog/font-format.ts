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

/** The trio a font pack's licence line reads off (PLAN T204) — shared shape between the CATALOG
 * detail wire (`CatalogEntryDetailDto.fontLicense`/`fontVersion`/`fontSubset`) and the installed
 * Wardrobe wire (`FontLibraryPackDto.license`/`version`/`subset`): different DTOs, same three field
 * names and the same "degrades gracefully, never blank" contract, so this shape only names what
 * `licenceLine` below actually needs. */
export interface FontLicenceFields {
  license: string | null;
  version: string | null;
  subset: string | null;
}

/** One pack's licence line — "&lt;licence&gt; · v&lt;version&gt; · &lt;subset&gt;" (PLAN T204,
 * mirrors the Library/Wardrobe page's own pre-existing line) — omitting whichever of the three is
 * absent (`version` is genuinely optional even on a cleanly-parsed manifest; `license`/`subset` are
 * `null` only when the manifest failed to parse, degrade, or — on the catalog wire — the entry is
 * unreachable). Degrades to "Licence unknown" rather than an empty line on the all-null edge — a
 * reviewer never sees a blank fact where the panel's whole point is showing this one. Shared by
 * `FontDetailPanel` (pre-install review) and the Wardrobe page's own pack cards so the two can never
 * drift on the same wording. */
export function licenceLine(fields: FontLicenceFields): string {
  const parts = [
    fields.license,
    fields.version !== null && fields.version !== "" ? `v${fields.version}` : null,
    fields.subset,
  ].filter((part): part is string => part !== null && part !== "");

  return parts.length > 0 ? parts.join(" · ") : "Licence unknown";
}
