"use client";

import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { formatFontByteTotal } from "./font-format";
import { prettifySlug } from "./format-slug";
import { SpecimenBlock } from "./SpecimenBlock";
import type { CatalogEntryDetailDto } from "./types";

export interface FontDetailPanelProps {
  slug: string;
  detail: CatalogEntryDetailDto;
  onInstallClick: () => void;
}

/**
 * A font pack's detail view (SPEC F104.3, F104.4, STORY-281, PLAN T202) — the click-through
 * `FontShelfCard` (T201) shipped inert without: pack name, description, family, and byte total off
 * the SAME `GET /api/catalog/entries/{slug}` detail fetch personas/themes already use (T194's
 * font-kind projection widens that response with `fontFamily`/`fontSpecimenFile`, both `null` for
 * every non-font entry), plus the real hash-verified `SpecimenBlock` (F104.4's "the real face").
 * Mirrors `ThemeDetailPanel`'s own shape one level up: name, descriptive text, then an Install
 * button that opens a confirm modal rather than issuing any request itself.
 *
 * <b>`detail.fontFamily` is rendered as plain text ONLY — never interpolated into CSS here</b> (the
 * T199/T200 stored-family obligation, re-stated for this consumer: `FontPackController`'s own
 * remarks note the STORED `family`/`style` columns are unbounded free-form prose, "whichever
 * reaches for either column in a CSS context first MUST NOT trust it as CSS-safe"). This panel
 * never reaches for it in a CSS context at all — the specimen below renders in a LOCAL,
 * self-generated family name instead (`SpecimenBlock`'s own remarks).
 *
 * <b>Install is a scope addition this task states plainly</b> (PLAN T202's own dispatch note): the
 * PLAN carries no dedicated install-button task for M1, and T204's own exit-check checklist
 * ("browse packs, open the Space Grotesk specimen, install, inspect the library") has no other UI
 * surface to install FROM. The T186 preview→confirm→POST precedent is the natural, minimal home,
 * so this panel's Install button opens `FontInstallModal` (confirm/cancel semantics mirrored from
 * `ThemeInstallModal`) rather than posting anything itself.
 */
export function FontDetailPanel({ slug, detail, onInstallClick }: FontDetailPanelProps): ReactNode {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="font-display text-[1.1rem] text-ink">{prettifySlug(slug)}</h2>
        {/* Install (scope addition, see this component's own remarks) opens FontInstallModal's
            confirm step — this click itself issues no request; the modal POSTs on confirm only. */}
        <Button type="button" variant="primary" onClick={onInstallClick}>
          Install
        </Button>
      </div>

      {detail.fontFamily !== null && detail.fontFamily !== "" && (
        <p className="text-[0.82rem] text-mute">Family: {detail.fontFamily}</p>
      )}

      {detail.fontByteTotal !== null && (
        <p className="text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
          {formatFontByteTotal(detail.fontByteTotal)}
        </p>
      )}

      {/* Plain text ONLY (mirrors DetailPanel's own persona-description rule, SPEC F90.6) — a bare
          `{detail.description}` JSX child, React's default escaping, never dangerouslySetInnerHTML. */}
      {detail.description !== null && detail.description !== "" && (
        <p className="text-[0.85rem] text-ink">{detail.description}</p>
      )}

      <SpecimenBlock slug={slug} specimenFile={detail.fontSpecimenFile} />
    </div>
  );
}
