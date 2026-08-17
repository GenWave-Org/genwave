"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { useState, type ChangeEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { readErrorMessage } from "@/lib/problem-details";
import { useRestoreFocus } from "@/lib/use-restore-focus";
import type { ThemeSummaryDto } from "./types";

const LABEL_CLASSES = "block text-[0.68rem] font-semibold uppercase tracking-[0.1em] text-accent-2";
const INPUT_CLASSES =
  "mt-1 h-9 w-full rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent disabled:opacity-50";

/** Lowercases, collapses every run of non `[a-z0-9]` into a single hyphen, and trims leading/trailing
 * hyphens — a CONVENIENCE default only (STORY-287 AC1's "confirmed with a name/slug"). The real gate
 * is server-side (`ThemesSaveAsOwnController`'s own `SlugFormat`, composed from
 * `CatalogIndexValidator.SlugSegment`) — this exists so the slug field starts pre-filled with
 * something that is USUALLY already valid, never to duplicate that gate; a slug this function cannot
 * make valid (all-symbol input, empty after collapsing) still surfaces the server's own 400 on
 * Confirm, exactly like every other field this dialog does not itself validate. */
function slugify(name: string): string {
  return name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

export interface SaveAsOwnResult {
  slug: string;
  name: string;
}

export interface SaveAsOwnModalProps {
  /** The ephemeral remix (SPEC F104.11/F104.12) — `EditorClient`'s own `remixManifest`: the base
   * theme's palette plus whatever faces were assigned, `slug`/`name`/`author` still the BASE theme's
   * own copied values until this modal's Confirm overrides them (see `handleConfirm`'s own remarks —
   * the route slug governing storage server-side, mirrored here so the request is honest about what
   * it is asking to create, is defense in depth, not the only thing preventing an accidental
   * overwrite of the base theme). */
  remix: ThemeSummaryDto;
  /** Every resolvable theme already in the base-theme picker (`EditorClient`'s own `themes` state,
   * the SAME `GET /api/themes` list) — used ONLY to detect whether the entered slug already names an
   * existing theme, for this dialog's own inline overwrite disclosure (PLAN T207 review finding F2).
   * A client-side courtesy, never the gate itself: `ThemesSaveAsOwnController`'s own fail-closed
   * 409 refusal for an imported target is the actual contract, unaffected by anything this array
   * does or does not know. */
  existingThemes: ThemeSummaryDto[];
  /** Slugs THIS SESSION already knows are authored (a previous save-as-own onto that exact slug,
   * `imported_from` null) — the only provenance signal available client-side without a `GET
   * /api/themes` wire change (SPEC F104.11's own "no field marks authorship" posture: `ThemeManifest`
   * carries no provenance, so neither does `ThemeSummaryDto`). A slug that matches
   * {@link existingThemes} but is NOT in this set is treated as provenance-unknown (could be
   * imported, could be shipped) — the fail-CLOSED default this disclosure follows: warn that the
   * server may refuse, rather than promise an update the server would go on to reject. */
  authoredSlugs: ReadonlySet<string>;
  onCancel: () => void;
  onSaved: (result: SaveAsOwnResult) => void;
}

type SaveStatus = { kind: "idle" } | { kind: "saving" } | { kind: "error"; message: string };

/**
 * Save-as-own's confirmation (SPEC F104.13, STORY-287, PLAN T207) — the T186/T202 preview-then-confirm
 * modal precedent (`ThemeInstallModal`/`FontInstallModal`), extended with the one field neither of
 * those needs: a name/slug the operator supplies for the NEW theme (a save has no catalog-sourced slug
 * to reuse the way an install does). The live scoped preview already showing behind this modal
 * (`ThemeDetailPreview`, rendered by `EditorClient`) IS the "review" half of the trust ruling — this
 * dialog only asks for the final name/slug plus go/no-go, mirroring `ThemeInstallModal`'s own "no
 * second review pane" posture.
 *
 * Opening this dialog issues no request of any kind; only Confirm does — the same F104.12 "ephemeral
 * until saved" guarantee `EditorClient`'s own remarks state, now paired with the one explicit,
 * operator-driven action that ends it.
 *
 * House modal conventions mirrored from `ThemeInstallModal`/`PersonaCardReviewModal` (Radix `Dialog`;
 * Cancel, Escape, and a backdrop click all route through the same `onOpenChange` → `onCancel` path;
 * hand-wired focus restoration since this component mounts fresh with no real `Dialog.Trigger` of its
 * own — see those components' own remarks for the full reasoning).
 */
export function SaveAsOwnModal({
  remix,
  existingThemes,
  authoredSlugs,
  onCancel,
  onSaved,
}: SaveAsOwnModalProps): ReactNode {
  // Deliberately NOT `remix.name`/`remix.slug` verbatim — those are still the BASE theme's own copied
  // values at this point (`buildRemixManifest`'s own "...base" spread), and a naive "Save" against an
  // unedited default would target the base theme's OWN slug, silently overwriting it the instant the
  // base itself is a saved/imported owner theme (a shipped base is caught by the server's own
  // shipped-slug 409, but an owner base is not). The "(Remix)" suffix makes the default itself a safe,
  // distinct slug — this field stays fully editable, this is a starting point, not a constraint.
  const [name, setName] = useState(`${remix.name} (Remix)`);
  const [slug, setSlug] = useState(slugify(`${remix.name} (Remix)`));
  // The slug field tracks the name field until the operator edits it directly — a derived field
  // that stops following its source the moment a human overrides it, never re-deriving over a slug
  // the operator already typed.
  const [slugTouched, setSlugTouched] = useState(false);
  const [status, setStatus] = useState<SaveStatus>({ kind: "idle" });

  const restoreFocus = useRestoreFocus("on-mount");

  function handleNameChange(event: ChangeEvent<HTMLInputElement>): void {
    const nextName = event.target.value;
    setName(nextName);
    if (!slugTouched) setSlug(slugify(nextName));
  }

  function handleSlugChange(event: ChangeEvent<HTMLInputElement>): void {
    setSlugTouched(true);
    setSlug(event.target.value);
  }

  async function handleConfirm(): Promise<void> {
    if (status.kind === "saving") return;
    setStatus({ kind: "saving" });

    // The operator-entered slug/name override the remix's own COPIED base-theme values (STORY-287
    // AC1) before this ever leaves the browser — the route slug still governs storage server-side
    // (ThemesSaveAsOwnController's own NormalizeSlug), but posting a body that already agrees with
    // the URL is what keeps this request honest, not merely "happens to be corrected by the server".
    // name.trim() is never "" here — the Confirm button that reaches this function is disabled
    // whenever canSave is false, and canSave itself requires name.trim() !== "" (N6 — a fallback to
    // remix.name for an empty trimmed name was unreachable dead code).
    const body = JSON.stringify({ ...remix, slug, name: name.trim() });

    try {
      const resp = await fetch(`/api/themes/${encodeURIComponent(slug)}/save-as-own`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body,
      });

      if (resp.ok) {
        onSaved((await resp.json()) as SaveAsOwnResult);
        return;
      }

      setStatus({ kind: "error", message: await readErrorMessage(resp) });
    } catch {
      setStatus({ kind: "error", message: "Network error — check your connection" });
    }
  }

  const canSave = slug.trim() !== "" && name.trim() !== "";

  // Overwrite disclosure (PLAN T207 review finding F2) — client courtesy only, computed fresh on
  // every render from the current slug field: does it already match an existing theme, and if so, is
  // that theme one THIS SESSION knows is authored (safe to update) or provenance-unknown (the server
  // will refuse an imported/shipped target, ThemesSaveAsOwnController's own fail-closed 409)?
  const existingMatch = existingThemes.find((theme) => theme.slug === slug);
  const overwriteState: "none" | "authored" | "unknown" =
    existingMatch === undefined ? "none" : authoredSlugs.has(slug) ? "authored" : "unknown";

  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) onCancel();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay
          data-testid="save-as-own-overlay"
          className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none"
        />
        <Dialog.Content
          aria-label="Save as own theme"
          className="fixed left-1/2 top-1/2 z-50 flex w-[calc(100%-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 flex-col rounded-[6px] border border-line bg-surface p-6 transition-opacity duration-200 ease-out focus:outline-none motion-reduce:transition-none"
          onCloseAutoFocus={restoreFocus.onCloseAutoFocus}
        >
          <Dialog.Title className="font-display text-[1.1rem] text-ink">Save as own theme</Dialog.Title>
          <Dialog.Description className="mt-1 text-[0.82rem] text-mute">
            Saves this remix as its own theme, selectable alongside every other. The theme it was mixed
            from is never changed — but saving under a slug that matches a DIFFERENT existing theme
            replaces that theme, not this one.
          </Dialog.Description>

          <div className="mt-4 flex flex-col gap-3">
            <div>
              <label htmlFor="save-as-own-name" className={LABEL_CLASSES}>
                Name
              </label>
              <input
                id="save-as-own-name"
                type="text"
                value={name}
                onChange={handleNameChange}
                disabled={status.kind === "saving"}
                className={INPUT_CLASSES}
              />
            </div>
            <div>
              <label htmlFor="save-as-own-slug" className={LABEL_CLASSES}>
                Slug
              </label>
              <input
                id="save-as-own-slug"
                type="text"
                value={slug}
                onChange={handleSlugChange}
                disabled={status.kind === "saving"}
                className={INPUT_CLASSES}
              />
            </div>
          </div>

          {overwriteState === "authored" && existingMatch && (
            <p role="status" className="mt-2 text-[0.8rem] text-mute">
              Will update your existing theme &quot;{existingMatch.name}&quot;.
            </p>
          )}
          {overwriteState === "unknown" && existingMatch && (
            <p role="status" className="mt-2 text-[0.8rem] text-danger">
              &quot;{existingMatch.name}&quot; already exists and may be imported or shipped — the server
              will refuse to overwrite it. Pick a different slug, or re-import to update it instead.
            </p>
          )}

          {status.kind === "error" && (
            <p role="alert" className="mt-3 text-[0.85rem] text-danger">
              {status.message}
            </p>
          )}

          <div className="mt-5 flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={onCancel} disabled={status.kind === "saving"}>
              Cancel
            </Button>
            <Button
              type="button"
              onClick={() => {
                void handleConfirm();
              }}
              disabled={status.kind === "saving" || !canSave}
            >
              {status.kind === "saving" ? "Saving…" : "Confirm save"}
            </Button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
