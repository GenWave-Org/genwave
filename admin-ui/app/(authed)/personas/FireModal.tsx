"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useRestoreFocus } from "@/lib/use-restore-focus";
import { PersonaExportLink } from "./PersonaExportLink";
import type { PersonaDto } from "./types";

export interface FireModalProps {
  /** The benched DJ this Fire confirm is for — a scheduled DJ never reaches this component at all
   * (`PersonasClient` renders no Fire/delete affordance on a Scheduled row; F91.9's FK guard would
   * 409 that delete anyway, so the UI doesn't offer a button that always fails). */
  persona: PersonaDto;
  /** True while the parent's own DELETE request is in flight — disables Cancel and relabels
   * Delete, mirroring `PersonaCardReviewModal`'s own `status.kind === "importing"` gating. */
  isFiring: boolean;
  /** Cancel, Escape, or a backdrop click — zero requests (STORY-247 AC4). */
  onCancel: () => void;
  /** Delete clicked with the export gate satisfied — the parent owns the actual
   * `DELETE /api/personas/{id}` call (same fetch/toast/list-splice home as every other mutation in
   * `PersonasClient`), including the RACE 409 case: the parent closes this modal on ANY outcome
   * (success or 409) and lets the existing toast carry the message. */
  onConfirmFire: () => void;
}

/**
 * The F94.2 export-first Fire confirm for a bench row (STORY-247 AC2/AC4) — replaces the generic
 * `useConfirm()` dialog for this one flow, the same way `PersonaCardReviewModal` replaces it for
 * the import review (house precedent for a rich modal; this component reuses its Radix Dialog
 * shell and focus-restore idiom, not its import-specific body).
 *
 * Export-gate affordance (T128 judgment call): Delete stays disabled until EITHER the operator
 * clicks the Export action rendered right here (`PersonaExportLink`, tracked via its own `onClick`
 * prop bound straight to the `<a>` — it's a same-origin download anchor, not a `fetch`, so there's
 * nothing to await before marking it "used") OR checks the "Skip export" box below it. A checkbox
 * is the smallest honest affordance for "I looked at this and chose not to export" — it names the
 * choice in the UI rather than silently timing out or hiding a bypass behind a second click on
 * Delete itself.
 */
export function FireModal({ persona, isFiring, onCancel, onConfirmFire }: FireModalProps): ReactNode {
  const [hasExported, setHasExported] = useState(false);
  const [skipExport, setSkipExport] = useState(false);
  const canDelete = hasExported || skipExport;

  // "Capture before the dialog steals focus" (gh-#465's shared hook) — this component mounts
  // fresh per open with no `Dialog.Trigger` Radix could refocus on its own.
  const restoreFocus = useRestoreFocus("on-mount");

  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) onCancel();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none" />
        <Dialog.Content
          aria-label={`Fire ${persona.name}`}
          className="fixed left-1/2 top-1/2 z-50 w-[calc(100%-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-[6px] border border-line bg-surface p-6 transition-opacity duration-200 ease-out focus:outline-none motion-reduce:transition-none"
          onCloseAutoFocus={restoreFocus.onCloseAutoFocus}
        >
          <Dialog.Title className="font-display text-[1.1rem] text-ink">
            Fire &quot;{persona.name}&quot;?
          </Dialog.Title>
          <Dialog.Description className="mt-2 text-[0.85rem] text-mute">
            This deletes {persona.name} permanently — the card, its memory, and everything it has
            learned about taste. This cannot be undone.
          </Dialog.Description>

          <div className="mt-4 flex flex-col gap-3 rounded-[6px] border border-line bg-surface-2 p-3">
            <div className="flex items-center gap-2">
              <PersonaExportLink persona={persona} onClick={() => setHasExported(true)} />
              {hasExported && (
                <span role="status" className="text-[0.78rem] text-mute">
                  Exported.
                </span>
              )}
            </div>
            <label className="flex min-h-10 items-center gap-2 text-[0.82rem] text-ink">
              <input
                type="checkbox"
                checked={skipExport}
                onChange={(e) => setSkipExport(e.currentTarget.checked)}
                className="h-4 w-4"
              />
              Skip export — I don&apos;t need this DJ&apos;s card.
            </label>
          </div>

          <div className="mt-6 flex justify-end gap-2">
            <Button variant="secondary" onClick={onCancel} disabled={isFiring}>
              Cancel
            </Button>
            <Button variant="destructive" onClick={onConfirmFire} disabled={!canDelete || isFiring}>
              {isFiring ? "Firing…" : "Delete"}
            </Button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
