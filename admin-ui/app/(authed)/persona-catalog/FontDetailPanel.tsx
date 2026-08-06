"use client";

import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { formatFontByteTotal, licenceLine } from "./font-format";
import { prettifySlug } from "./format-slug";
import { SpecimenBlock } from "./SpecimenBlock";
import type { CatalogEntryDetailDto } from "./types";

export interface FontDetailPanelProps {
  slug: string;
  detail: CatalogEntryDetailDto;
  /** Whether THIS slug already has an installed pack (PLAN T204, Dean's post-v3.1.0 review:
   * reopening an installed pack's detail panel showed no sign it was already installed). Sourced
   * from `GET /api/fonts`'s own listing — see `PersonaCatalogClient`'s own remarks for where that
   * read happens and how a fresh install flips this without a reload. */
  isInstalled: boolean;
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
 *
 * <b>Licence line (PLAN T204, Dean's post-v3.1.0 review).</b> "&lt;licence&gt; · v&lt;version&gt; ·
 * &lt;subset&gt;" via the shared `licenceLine` helper (`font-format.ts`) — the SAME line the
 * Wardrobe page's own installed-pack cards render, so the one trust fact a PRE-install review most
 * needs (what licence am I about to agree to?) reads identically whether the pack is already
 * installed or not. Degrades to "Licence unknown" rather than an empty line — see that helper's own
 * remarks.
 *
 * <b>Installed-state awareness (PLAN T204).</b> Reopening an already-installed pack's detail panel
 * used to show no sign of that — `SpecimenBlock`'s OLD "Admin-only specimen — not installed" caption
 * read as a status claim it never was (it only ever described the SPECIMEN, an always-transient
 * preview, never the pack itself), so that caption is now state-neutral in BOTH states (see
 * `SpecimenBlock`'s own remarks) and the installed signal moved here instead. `isInstalled` (sourced
 * by `PersonaCatalogClient` from `GET /api/fonts`, see its own remarks) drives an "Installed" chip —
 * the same quiet bordered-pill treatment the Wardrobe page's own provenance chip uses — and the
 * button's own label: "Re-install" when a pack under this slug is already installed
 * (`FontPackController.Install` upserts, PLAN T199, so a re-install is a genuinely supported,
 * non-destructive action), "Install" otherwise.
 */
export function FontDetailPanel({ slug, detail, isInstalled, onInstallClick }: FontDetailPanelProps): ReactNode {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="font-display text-[1.1rem] text-ink">{prettifySlug(slug)}</h2>
          {isInstalled && <InstalledChip />}
        </div>
        {/* Install/Re-install (scope addition, see this component's own remarks) opens
            FontInstallModal's confirm step — this click itself issues no request; the modal POSTs
            on confirm only. */}
        <Button type="button" variant="primary" onClick={onInstallClick}>
          {isInstalled ? "Re-install" : "Install"}
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

      <p className="text-[0.75rem] text-mute">
        {licenceLine({ license: detail.fontLicense, version: detail.fontVersion, subset: detail.fontSubset })}
      </p>

      {/* Plain text ONLY (mirrors DetailPanel's own persona-description rule, SPEC F90.6) — a bare
          `{detail.description}` JSX child, React's default escaping, never dangerouslySetInnerHTML. */}
      {detail.description !== null && detail.description !== "" && (
        <p className="text-[0.85rem] text-ink">{detail.description}</p>
      )}

      <SpecimenBlock slug={slug} specimenFile={detail.fontSpecimenFile} />
    </div>
  );
}

/** "Installed" chip (PLAN T204) — the SAME quiet bordered-pill treatment the Wardrobe page's own
 * `ProvenanceChip` uses (`app/(authed)/wardrobe/WardrobeClient.tsx`), reused here as a plain status
 * marker rather than a provenance stamp (no slug/date — this panel already names the slug in its own
 * heading): a genuine shared component would need editing both files for a shape that already
 * differs (provenance text vs a bare status word), the same reasoning that chip's own remarks give
 * for not sharing with the persona/theme chips either. */
function InstalledChip(): ReactNode {
  return (
    <span className="inline-flex w-fit items-center rounded-[3px] border border-line px-1.5 py-0.5 text-[0.68rem] text-mute">
      Installed
    </span>
  );
}
