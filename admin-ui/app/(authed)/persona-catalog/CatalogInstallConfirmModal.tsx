"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { prettifySlug } from "./format-slug";

/** The outcome one kind's own `onConfirm` reports back to this shell — `ok: true` once the caller
 * has ALREADY called its own `onInstalled` (this modal never calls it itself, each kind's own
 * success body shape differs too much to generalize that part); `ok: false` carries the message
 * the inline error renders. */
export type CatalogInstallOutcome = { ok: true } | { ok: false; message: string };

export interface CatalogInstallConfirmModalProps {
  /** The catalog entry's own slug — the dialog title always reads `Install "&lt;prettified
   * slug&gt;"?`, the one piece of copy every kind's pre-extraction modal (Theme/Font/Avatar) shared
   * verbatim. */
  slug: string;
  /** `Dialog.Content`'s own `aria-label`, e.g. `"Install theme"` / `"Install icon pack"`. */
  ariaLabel: string;
  /** The overlay's own `data-testid` root — rendered as `${testId}-overlay`, mirroring each
   * pre-extraction modal's own mark (`theme-install-overlay`, `font-install-overlay`, …). */
  testId: string;
  /** The one line of kind-specific copy under the title — differs per kind (a theme "adopts... for
   * anyone who selects it", a font/avatar/icon pack "fetches and stores... immediately"). */
  description: ReactNode;
  onCancel: () => void;
  /**
   * Issues the actual install request when Confirm is pressed and reports the outcome. Each kind
   * owns its own endpoint, request body (a theme POSTs its manifest text; font/avatar/icon POST no
   * body — every byte is fetched server-side), and success-body shape/`onInstalled` mapping — this
   * modal owns ONLY the idle/installing/error UI state machine and the Radix/focus wiring every
   * kind shared byte-for-byte before this extraction (PLAN T304 rider: Theme/Font/Avatar modals
   * differed by ~14 lines total, all of it now living in each kind's own thin wrapper below this
   * shell). Runs to completion — including the caller's own `onInstalled` call on success — before
   * ever returning `{ ok: true }`.
   */
  onConfirm: () => Promise<CatalogInstallOutcome>;
}

type ConfirmStatus = { kind: "idle" } | { kind: "installing" } | { kind: "error"; message: string };

/**
 * The catalog install confirmation shell (SPEC F90.7/F103.6/F104.5/F128.3/F130.5, PLAN T304) — the
 * trust ruling's "review, then explicitly confirm" stop (ARCHITECTURE.md "Trust ruling"), shared by
 * every kind's own install modal (`ThemeInstallModal`/`FontInstallModal`/`AvatarInstallModal`/
 * `IconInstallModal`): opening this dialog issues no request of any kind; only Confirm does, and
 * Cancel/Escape/a backdrop click all close it with none either. House modal conventions (Radix
 * `Dialog`; Cancel/Escape/backdrop-click all route through the same `onOpenChange` → `onCancel`
 * path; hand-wired focus restoration since this component mounts fresh with no real
 * `Dialog.Trigger` of its own — mirrors `PersonaCardReviewModal`'s own reasoning, restated at each
 * pre-extraction modal this shell replaces).
 *
 * A failed confirm (`onConfirm` resolving `{ ok: false }`) leaves this dialog OPEN with the inline
 * error shown — the operator can retry or cancel; a successful confirm (`{ ok: true }`) also leaves
 * the CLOSING itself to the caller: this component sets no "closed" state of its own, the parent
 * unmounts it once its own `onInstalled` has already run (the same "success returns, caller
 * unmounts" shape every pre-extraction modal used).
 */
export function CatalogInstallConfirmModal({
  slug,
  ariaLabel,
  testId,
  description,
  onCancel,
  onConfirm,
}: CatalogInstallConfirmModalProps): ReactNode {
  const [status, setStatus] = useState<ConfirmStatus>({ kind: "idle" });

  const restoreFocusRef = useRef<HTMLElement | null | undefined>(undefined);
  if (restoreFocusRef.current === undefined) {
    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  }

  async function handleConfirm(): Promise<void> {
    if (status.kind === "installing") return;
    setStatus({ kind: "installing" });

    const outcome = await onConfirm();
    if (!outcome.ok) {
      setStatus({ kind: "error", message: outcome.message });
    }
    // `{ ok: true }`: the caller already called its own onInstalled; this component leaves status
    // at "installing" — about to unmount, so there is nothing left for "idle" to buy here.
  }

  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) onCancel();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay
          data-testid={`${testId}-overlay`}
          className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none"
        />
        <Dialog.Content
          aria-label={ariaLabel}
          className="fixed left-1/2 top-1/2 z-50 flex w-[calc(100%-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 flex-col rounded-[6px] border border-line bg-surface p-6 transition-opacity duration-200 ease-out focus:outline-none motion-reduce:transition-none"
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            restoreFocusRef.current?.focus();
          }}
        >
          <Dialog.Title className="font-display text-[1.1rem] text-ink">
            Install &quot;{prettifySlug(slug)}&quot;?
          </Dialog.Title>
          <Dialog.Description className="mt-1 text-[0.82rem] text-mute">{description}</Dialog.Description>

          {status.kind === "error" && (
            <p role="alert" className="mt-3 text-[0.85rem] text-danger">
              {status.message}
            </p>
          )}

          <div className="mt-5 flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={onCancel} disabled={status.kind === "installing"}>
              Cancel
            </Button>
            <Button
              type="button"
              onClick={() => {
                void handleConfirm();
              }}
              disabled={status.kind === "installing"}
            >
              {status.kind === "installing" ? "Installing…" : "Confirm install"}
            </Button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
