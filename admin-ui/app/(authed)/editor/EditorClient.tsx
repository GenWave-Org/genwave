"use client";

import { useState, type ChangeEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { toast } from "@/components/ui/toast";
import { ThemeDetailPreview } from "../persona-catalog/ThemeDetailPreview";
import { SaveAsOwnModal, type SaveAsOwnResult } from "./SaveAsOwnModal";
import type { AssignableFaceDto, ThemeFontFaceDto, ThemeSummaryDto } from "./types";

export interface EditorClientProps {
  /** Every resolvable theme (`GET /api/themes`, SPEC F104.11) — the base-theme picker's candidate
   * list. Full manifests, not labels: the editor needs each candidate's own palette/current fonts to
   * seed the remix the moment a base theme is picked. */
  themes: ThemeSummaryDto[];
  /** The role pickers' ENTIRE assignable face set (`GET /api/fonts/assignable`, SPEC F104.11; widened
   * at T206 review finding F4) — vendored ∪ installed, one row per family, already deduped and
   * representative-face-resolved SERVER-SIDE (`FontPackController.Assignable`). This component trusts
   * it verbatim and derives nothing of its own: before the F4 fix this file separately re-merged a
   * raw installed-pack list with its own `style === "normal"` heuristic, which could disagree with
   * the server's filename-based vendored heuristic — one derivation now, not two that could drift
   * apart. */
  assignableFaces: AssignableFaceDto[];
}

const SELECT_CLASSES =
  "mt-1 h-9 w-full rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent";

const LABEL_CLASSES = "block text-[0.68rem] font-semibold uppercase tracking-[0.1em] text-accent-2";

/** A theme's own currently-declared face for a role, as the picker's "currently selected" proxy for
 * an UNASSIGNED role (SPEC F104.11 AC1: the preview already shows something the moment a base theme
 * is picked, before any explicit assignment) — NEVER used to build the remix manifest itself, which
 * threads the base theme's own `ThemeFontFaceDto` through byte-untouched when unassigned (see
 * `buildRemixManifest`'s own remarks, review finding F3). `assets[0]?.src` only falls back to `""`
 * because a theme's own asset list is typed as a plain array here (`ThemeManifestParser`'s own
 * non-empty gate is a server-side invariant this type doesn't carry) — not a real case in practice. */
function currentFaceOption(face: ThemeFontFaceDto): AssignableFaceDto {
  return { family: face.family, src: face.assets[0]?.src ?? "" };
}

/** Ensures a role picker's own "currently selected" value always corresponds to a real `<option>`
 * (review finding F4: "the base theme's current face is not among the options" case). A base theme's
 * own declared face can legitimately fall outside the assignable set this component otherwise offers
 * (e.g. it references an italic variant, which the assignable set never lists as its own row — see
 * `FontPackController.Assignable`'s own "one representative face per family" remarks) — appending
 * `current` when it is missing means the `<select>` shows the TRUE current selection rather than
 * silently defaulting to whatever a browser does when a controlled `value` matches no `<option>`
 * (blank/none-selected, which would lie about what the preview is actually composing). Skips the
 * append when `current.src` is the defensive `""` fallback above (nothing real to show as an option).
 */
function withCurrentOption(options: AssignableFaceDto[], current: AssignableFaceDto): AssignableFaceDto[] {
  if (current.src === "" || options.some((option) => option.src === current.src)) return options;
  return [...options, current];
}

/** The single-face shape an EXPLICIT role assignment always produces (SPEC F104.11's "component mix
 * only" — one face, weight 400, style normal, regardless of what the picked family's own richer
 * declaration might otherwise offer). Never used for an unassigned role — see `buildRemixManifest`'s
 * own remarks (review finding F3). */
function assignedFace(option: AssignableFaceDto): ThemeFontFaceDto {
  return { family: option.family, assets: [{ src: option.src, weight: "400", style: "normal" }] };
}

/** Builds the ephemeral remix (SPEC F104.11/F104.12): the base theme's own palette (`modes`) and
 * identity (`slug`/`name`/`author`) untouched. Each role's font declaration is EITHER the base
 * theme's OWN, byte-untouched (no override — nothing was ever assigned, so nothing degrades: review
 * finding F3, the fix for a bug where an unmodified base theme previewed with its weight RANGE
 * collapsed to a bare "400" and any italic asset silently dropped, even though nothing was ever
 * assigned) OR the single-face 400/normal shape `assignedFace` produces (an override IS present — an
 * explicit assignment was made, SPEC F104.11's "component mix only" scope, deliberately narrower than
 * a base theme's own declaration can be). A plain object transform, never a network round trip of its
 * own — this function itself still writes nothing anywhere; its result reaches TWO consumers, both
 * POSTs a human explicitly triggers: `POST /api/themes/preview` (via `ThemeDetailPreview`'s own
 * `manifestText` prop, `JSON.stringify`d, on every assignment) and, once Save-as-own is confirmed
 * (SPEC F104.13, PLAN T207 — `SaveAsOwnModal`), `POST /api/themes/{slug}/save-as-own` with the
 * operator-supplied name/slug substituted in. */
function buildRemixManifest(
  base: ThemeSummaryDto,
  displayOverride: AssignableFaceDto | null,
  sansOverride: AssignableFaceDto | null
): ThemeSummaryDto {
  return {
    ...base,
    fonts: {
      display: displayOverride ? assignedFace(displayOverride) : base.fonts.display,
      sans: sansOverride ? assignedFace(sansOverride) : base.fonts.sans,
    },
  };
}

interface RolePickerProps {
  id: string;
  label: string;
  options: AssignableFaceDto[];
  value: string;
  onAssign: (option: AssignableFaceDto) => void;
}

/** One role's face picker — the editor's own assignable set, as-is, family name as the visible
 * option text. `family` DOES eventually reach a real stylesheet once its option is assigned to a role
 * — see `AssignableFaceDto`'s own remarks (`types.ts`) for the full trace and the actual gate that makes
 * that safe: server-side, at `POST /api/themes/preview`'s `ThemeManifestParser.FontFamilyPattern`
 * check, which re-validates every family before `ThemeCssComposer` ever composes it. Here `family` is
 * plain React text content only — this side of the wire closes nothing on its own (review finding
 * F2, correcting this comment's former, incorrect "never interpolated into a stylesheet" claim). */
function RolePicker({ id, label, options, value, onAssign }: RolePickerProps): ReactNode {
  function handleChange(event: ChangeEvent<HTMLSelectElement>): void {
    const chosen = options.find((option) => option.src === event.target.value);
    if (chosen !== undefined) onAssign(chosen);
  }

  return (
    <div>
      <label htmlFor={id} className={LABEL_CLASSES}>
        {label}
      </label>
      <select id={id} className={SELECT_CLASSES} value={value} onChange={handleChange}>
        {options.map((option) => (
          <option key={option.src} value={option.src}>
            {option.family}
          </option>
        ))}
      </select>
    </div>
  );
}

/**
 * The v2 editor (SPEC F104.11/F104.12, STORY-286, PLAN T206): a base-theme picker plus a face-per-
 * role picker (display/sans, vendored ∪ installed), composing a transient scoped live preview
 * through the SAME `POST /api/themes/preview` mechanism the theme catalog's own detail preview uses
 * (T186, `ThemeDetailPreview` reused verbatim below, not re-implemented). Assigning a face updates
 * `displayOverride`/`sansOverride` react state, which recomposes `remixManifest` below, which
 * `ThemeDetailPreview`'s own `manifestText` prop change re-triggers its POST effect from — the ONLY
 * network call this whole page ever issues client-side (every other value here is a server-fetched
 * prop, never re-fetched on this side of the wire).
 *
 * <b>Ephemeral by construction, not by discipline (SPEC F104.12).</b> `displayOverride`/
 * `sansOverride`/`baseSlug` are plain `useState` — no cookie, no `localStorage`/`sessionStorage`
 * write anywhere in this file. Closing the editor (navigating away) or reloading the page discards
 * every assignment: there is nothing to revert BECAUSE there is nothing that outlives the component.
 * "Revert" has no affordance of its own (SPEC F104.12's own "reverting is closing the editor") — a
 * fresh page load always starts from each theme's own shipped/imported/saved fonts.
 */
export function EditorClient({ themes: initialThemes, assignableFaces }: EditorClientProps): ReactNode {
  // Seeded from the server-fetched prop, then grown locally the moment a save succeeds (SPEC F104.13
  // "immediately selectable") — never shrunk, never re-fetched: a saved theme's row is real the
  // instant the response lands, so reflecting it here needs no round trip back to GET /api/themes.
  const [themes, setThemes] = useState<ThemeSummaryDto[]>(initialThemes);
  const [baseSlug, setBaseSlug] = useState<string | undefined>(initialThemes[0]?.slug);
  const [displayOverride, setDisplayOverride] = useState<AssignableFaceDto | null>(null);
  const [sansOverride, setSansOverride] = useState<AssignableFaceDto | null>(null);
  const [showSaveModal, setShowSaveModal] = useState(false);
  // Every slug a save-as-own THIS SESSION has already written — SaveAsOwnModal's own "authored,
  // safe to update" disclosure (PLAN T207 review finding F2). Grown alongside `themes` in
  // `handleSaved` below, never shrunk or re-derived: a fresh GET /api/themes carries no provenance
  // field to read this back from (SPEC F104.11's own "no field marks authorship" posture), so this
  // is the only ground truth the client has for "authored" versus "provenance unknown".
  const [authoredSlugs, setAuthoredSlugs] = useState<ReadonlySet<string>>(new Set());

  function handleBaseThemeChange(nextSlug: string): void {
    // A new base theme resets both role overrides back to ITS OWN fonts (not the previous base
    // theme's), the same "picking a new base starts from its own look" default AC1 describes.
    setBaseSlug(nextSlug);
    setDisplayOverride(null);
    setSansOverride(null);
  }

  if (themes.length === 0) {
    return (
      <EmptyState
        title="No themes available"
        reason="No resolvable theme could be loaded — check the station's connection and try again."
      />
    );
  }

  const baseTheme = themes.find((theme) => theme.slug === baseSlug) ?? themes[0];
  if (baseTheme === undefined) return null; // unreachable: themes.length > 0 above guarantees this

  const displaySelected = displayOverride ?? currentFaceOption(baseTheme.fonts.display);
  const sansSelected = sansOverride ?? currentFaceOption(baseTheme.fonts.sans);
  const remixManifest = buildRemixManifest(baseTheme, displayOverride, sansOverride);

  /** SPEC F104.13's "immediately selectable and resolvable" made visible in THIS session too, not
   * only provable via a fresh page load: the saved row (this session's own posted manifest, slug/name
   * substituted for the response's own — the server's actual upsert key, never assumed to match what
   * the modal asked for) joins the base-theme picker and becomes the new selection, its own role
   * overrides cleared exactly like picking any other base theme does (`handleBaseThemeChange`'s own
   * "starts from its own look" rule). A same-slug re-save (the operator saving twice under one name)
   * replaces rather than duplicates the picker entry — station.theme's own upsert-by-slug contract,
   * mirrored here. */
  function handleSaved(result: SaveAsOwnResult): void {
    const saved: ThemeSummaryDto = { ...remixManifest, slug: result.slug, name: result.name };
    setThemes((previous) => [...previous.filter((theme) => theme.slug !== saved.slug), saved]);
    setAuthoredSlugs((previous) => new Set(previous).add(result.slug));
    handleBaseThemeChange(saved.slug);
    setShowSaveModal(false);
    toast.success(`"${saved.name}" saved — selectable now.`);
  }

  return (
    <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
      <div className="flex w-full max-w-sm flex-col gap-4">
        <div>
          <label htmlFor="editor-base-theme" className={LABEL_CLASSES}>
            Base theme
          </label>
          <select
            id="editor-base-theme"
            className={SELECT_CLASSES}
            value={baseTheme.slug}
            onChange={(event) => handleBaseThemeChange(event.target.value)}
          >
            {themes.map((theme) => (
              <option key={theme.slug} value={theme.slug}>
                {theme.name}
              </option>
            ))}
          </select>
        </div>

        <RolePicker
          id="editor-display-face"
          label="Display face"
          options={withCurrentOption(assignableFaces, displaySelected)}
          value={displaySelected.src}
          onAssign={setDisplayOverride}
        />

        <RolePicker
          id="editor-sans-face"
          label="Sans face"
          options={withCurrentOption(assignableFaces, sansSelected)}
          value={sansSelected.src}
          onAssign={setSansOverride}
        />

        <Button type="button" onClick={() => setShowSaveModal(true)} className="self-start">
          Save as own
        </Button>
      </div>

      {showSaveModal && (
        <SaveAsOwnModal
          remix={remixManifest}
          existingThemes={themes}
          authoredSlugs={authoredSlugs}
          onCancel={() => setShowSaveModal(false)}
          onSaved={handleSaved}
        />
      )}

      <div className="w-full max-w-sm">
        <ThemeDetailPreview slug={baseTheme.slug} manifestText={JSON.stringify(remixManifest)} />
      </div>
    </div>
  );
}
