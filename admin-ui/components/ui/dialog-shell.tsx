"use client";

import * as Dialog from "@radix-ui/react-dialog";
import type { ReactNode } from "react";

export interface DialogShellProps {
  /** Whether the dialog is visually open. `ConfirmDialogProvider` keeps its own `Dialog.Root`
   * mounted permanently and flips this per-request (so a call to `useConfirm()` never re-mounts
   * the dialog); every OTHER consumer of this shell (`PersonaOfferDialog`, and by the same house
   * pattern `ThemeInstallModal`/`FontInstallModal`/`PersonaCardReviewModal`/`ShowCardReviewModal`
   * elsewhere, though those stay on their own bespoke markup — see this file's own remarks) instead
   * mounts fresh only while needed, with `open` hardcoded `true` and the PARENT unmounting the
   * whole component on close. Both shapes read `open` identically here; only the caller's own
   * mount lifecycle differs, and this shell doesn't need to know which one it's in.
   */
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Forwarded straight to `Dialog.Content` — each caller owns ITS OWN `useRestoreFocus()` call
   * and picks its capture timing there (see this file's own remarks for why that can't be
   * centralised here). */
  onCloseAutoFocus: (event: Event) => void;
  children: ReactNode;
}

/**
 * The plain, provider-free Radix `Dialog` shell (SPEC F28.9's confirm-dialog styling) shared by
 * `ConfirmDialogProvider`'s own imperative `useConfirm()` dialog and `PersonaOfferDialog` (PLAN
 * T255 review finding F4) — both render the IDENTICAL overlay/content chrome (fixed centering,
 * 6px radius, `--line` border, `--surface` fill, the same transition/motion-reduce pair) for a
 * small yes/no prompt; only the content INSIDE it (title/description/footer buttons) differs per
 * caller, so this component owns exactly the outer shell and nothing about WHAT is being
 * confirmed. Mirrors `catalog-badges.tsx`'s own extraction reasoning (PLAN T255): a component with
 * more than one real consumer earns a shared home instead of a second, drifting copy.
 *
 * Deliberately narrower than a generic "every modal in this codebase" shell: `PersonaCardReviewModal`/
 * `ThemeInstallModal`/`FontInstallModal`/`ShowCardReviewModal` size their own content differently
 * (a scrollable `max-w-xl` body for a full-card review vs. this shell's fixed `max-w-sm` yes/no),
 * and carry their own `aria-label`/`data-testid` overlay marks those simpler prompts don't need —
 * folding all of them into one shell would trade a real, small duplication for a false generality
 * this task never asked for (YAGNI). This shell exists for exactly the TWO callers review finding
 * F4 named.
 *
 * <b>No `useConfirm()`/context of any kind here</b> — `PersonaOfferDialog`'s own "provider-free
 * calling shape" (the reason review finding F1/F4 rejected reusing `useConfirm()` directly: several
 * pre-existing specs render `PersonaCatalogClient` with no `ConfirmDialogProvider` ancestor) is
 * preserved exactly: this shell takes plain props, nothing from React context, and neither
 * requires nor provides one.
 *
 * <b>Focus restoration is NOT owned by this shell</b> (`onCloseAutoFocus` is a required prop, not
 * internal state): `ConfirmDialogProvider`'s own `Dialog.Root` mounts once for the whole authed
 * shell and must capture `document.activeElement` at EACH `confirm()` call (an imperative capture
 * outside the render path); every other consumer instead mounts fresh per-open and captures once,
 * lazily, on that first render. A ref this shell owned internally could only implement ONE of
 * those two timings correctly — sharing the STYLING while leaving the CAPTURE TIMING to each
 * caller (who already differs on it) is the honest boundary, not a false unification. The
 * mechanics of both timings now live in ONE place — `useRestoreFocus()` in
 * `lib/use-restore-focus.ts` (gh-#465), parameterized `"imperative"` vs `"on-mount"` — but the
 * CHOICE stays with each caller, which is why this prop remains required rather than shell-owned.
 */
export function DialogShell({ open, onOpenChange, onCloseAutoFocus, children }: DialogShellProps): ReactNode {
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none" />
        <Dialog.Content
          className="fixed left-1/2 top-1/2 z-50 w-[calc(100%-2rem)] max-w-sm -translate-x-1/2 -translate-y-1/2 rounded-[6px] border border-line bg-surface p-6 transition-opacity duration-200 ease-out focus:outline-none motion-reduce:transition-none"
          onCloseAutoFocus={onCloseAutoFocus}
        >
          {children}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
