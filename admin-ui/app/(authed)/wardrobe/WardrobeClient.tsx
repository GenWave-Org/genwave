"use client";

import type { ReactNode } from "react";
import { EmptyState } from "@/components/ui/empty-state";
import { formatDateStamp } from "@/lib/format-clock";
import { formatFontByteTotal, licenceLine } from "../persona-catalog/font-format";
import type { FontLibraryPackDto } from "./types";

export interface WardrobeClientProps {
  /** Every installed pack, from `GET /api/fonts` (SPEC F104.7). */
  packs: FontLibraryPackDto[];
  /** Test-only injection point for the provenance chip's `formatDateStamp` call; production omits
   * this and gets the browser's local zone — the same PersonasClient/SettingsForm idiom (T105/T187),
   * not a bespoke one. */
  timeZone?: string;
  /**
   * Whether the Community Catalog is currently enabled (SPEC F90.1, `Community:CatalogIndexUrl`
   * non-empty) — swaps the empty-state CTA (PLAN T203 review finding F3). The Wardrobe nav item is
   * deliberately ungated (see nav-items.ts's own remarks: installed packs outlive the catalog,
   * F104.8), so a disabled catalog must never leave this page's own empty state pointing at
   * `/persona-catalog` — that page 404s off-catalog, the exact dead end the ungated nav exists to
   * avoid landing an operator in. Disabled swaps the CTA for a pointer at Settings instead. Defaults
   * to `false` — fail closed, matching Sidebar/MobileNav's own `catalogEnabled` convention: an
   * isolated render with no live signal offers no browse promise it cannot keep.
   */
  catalogEnabled?: boolean;
}

/** Provenance chip — "Installed · &lt;slug&gt; · &lt;date&gt;" (SPEC F104.7 AC1, the db/25 pattern)
 * — mirrors `PersonasClient`'s own `ProvenanceBadge`/`SettingsForm`'s own `ThemeProvenanceBadge`
 * treatment (quiet bordered chip, T105/T187) rather than importing either: this page sits outside
 * both files' own partitions, and the shape here ("Installed", no leading label) differs from both
 * ("Hired"/"&lt;label&gt; — Imported") enough that a genuine shared component would need editing
 * either file anyway. `importedFrom` renders VERBATIM — this is provenance, not decoration, same
 * rule the persona/theme chips already follow — even though it is always equal to the pack's own
 * `slug` today (a pack has no authored-in-place path); reading it off its own field rather than
 * `pack.slug` keeps this chip honest about which column IS the provenance stamp. */
function ProvenanceChip({
  importedFrom,
  importedAt,
  timeZone,
}: {
  importedFrom: string;
  importedAt: string;
  timeZone?: string;
}): ReactNode {
  return (
    <span className="inline-flex w-fit items-center rounded-[3px] border border-line px-1.5 py-0.5 text-[0.68rem] text-mute">
      {`Installed · ${importedFrom} · ${formatDateStamp(importedAt, { timeZone })}`}
    </span>
  );
}

/**
 * The Wardrobe page's client half (SPEC F104.7, STORY-284, PLAN T203; renamed "Library" → "Wardrobe"
 * at PLAN T204, Dean's ruling — nav label and route only, see nav-items.ts's own remarks; the wire
 * (`GET /api/fonts`, `FontLibraryPackDto`) keeps its name, backend DTOs don't chase UI labels) —
 * every installed font pack, each rendered as its own card: family (title), faces with style + byte
 * size (the shared `font-format.ts` helper, mirroring the shelf card's own T201 byte-total
 * treatment), the licence line, and the "Installed · ⟨slug⟩ · ⟨date⟩" provenance chip (AC1).
 * Read-only — this page lists what T199's install route already wrote; it issues no requests of its
 * own. On an empty wardrobe, `catalogEnabled` picks the empty-state CTA (T203 review finding F3) —
 * see that prop's own remarks.
 *
 * <b>PLAIN TEXT ONLY (the T199/T200 stored-family/style obligation, closed here).</b>
 * `pack.family` and each face's `style` are unbounded free-form prose (see `FontLibraryPackDto`'s
 * own remarks) — both render as bare React text-node children ONLY, React's default escaping,
 * never `dangerouslySetInnerHTML` and never interpolated into an inline `style` attribute or any
 * other CSS context anywhere on this page.
 */
export function WardrobeClient({ packs, timeZone, catalogEnabled = false }: WardrobeClientProps): ReactNode {
  if (packs.length === 0) {
    return catalogEnabled ? (
      <EmptyState
        title="No packs installed"
        reason="Browse the Community Catalog to install a font pack for this station."
        cta={{ label: "Browse the Community Catalog", href: "/persona-catalog" }}
      />
    ) : (
      <EmptyState
        title="No packs installed"
        reason="The Community Catalog is disabled — enable Community:CatalogIndexUrl in Settings to browse packs."
        cta={{ label: "Open Settings", href: "/settings" }}
      />
    );
  }

  return (
    <ul aria-label="Installed font packs" className="flex flex-col gap-3">
      {packs.map((pack) => (
        <li key={pack.slug} className="rounded-[6px] border border-line bg-surface p-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 className="font-display text-[1.1rem] text-ink">{pack.family}</h2>
            <ProvenanceChip importedFrom={pack.importedFrom} importedAt={pack.importedAt} timeZone={timeZone} />
          </div>

          <ul className="mt-3 flex flex-col gap-1 text-[0.85rem] text-ink">
            {pack.faces.map((face) => (
              <li key={face.file}>
                {face.style} — {formatFontByteTotal(face.byteSize)}
              </li>
            ))}
          </ul>

          <p className="mt-2 text-[0.75rem] text-mute">{licenceLine(pack)}</p>
        </li>
      ))}
    </ul>
  );
}
