"use client";

import { useCallback, useMemo, useRef } from "react";

/**
 * When {@link useRestoreFocus} captures `document.activeElement` as the restore target (gh-#465).
 * Fixed for the lifetime of the component — no caller has a reason to switch timings mid-flight,
 * and the on-mount capture below has already happened by the second render anyway.
 *
 * - `"on-mount"` — captured once, inline, during the component's FIRST render, before Radix's
 *   `FocusScope` mount effect ever moves focus into the dialog. For modals the parent mounts
 *   fresh per open and unmounts wholesale on close (`FireModal`, `PersonaCardReviewModal`, …):
 *   whatever held focus as that first render ran IS the element that opened them.
 * - `"imperative"` — captured only when the caller invokes {@link UseRestoreFocusResult.capture},
 *   once per open request. For an always-mounted dialog (`ConfirmDialogProvider`'s single
 *   persistent `Dialog.Root`): its mount-time `activeElement` says nothing about which element
 *   will call `confirm()` minutes later, so the capture must ride the request itself.
 */
export type RestoreFocusTiming = "on-mount" | "imperative";

export interface UseRestoreFocusResult {
  /** Re-captures `document.activeElement` NOW as the restore target — `"imperative"` timing's
   * whole point: call it at the exact moment a request opens the dialog (e.g. inside
   * `confirm()`), while focus is still on the element that asked. `"on-mount"` callers never
   * call this — the hook already captured during first render, and any later call would
   * overwrite that good target with one already inside the open dialog. */
  capture: () => void;
  /** Forward verbatim to `Dialog.Content`/`DialogShell`'s `onCloseAutoFocus`: prevents Radix's
   * default close behaviour (refocusing a `<Dialog.Trigger>` none of these callers render) and
   * hands focus back to the captured element by hand. A no-op when nothing was captured. */
  onCloseAutoFocus: (event: Event) => void;
}

/** `null`, not the raw `activeElement`, when nothing focusable held focus — `?.focus()` on close
 * then quietly restores nothing, exactly what every pre-extraction copy did. */
function captureActiveElement(): HTMLElement | null {
  return document.activeElement instanceof HTMLElement ? document.activeElement : null;
}

/**
 * The restore-focus-on-close block every house modal used to hand-copy (gh-#465; the T255 review
 * flagged the copies) — extracted here VERBATIM, parameterized only on the one axis the copies
 * genuinely differed on: WHEN the pre-open `document.activeElement` gets captured (see
 * {@link RestoreFocusTiming}). That axis is exactly why `DialogShell` (T255) deliberately does
 * not own this ref itself — a shell-internal ref could only implement one timing; see that
 * component's own remarks.
 *
 * Why hand-wired at all: Radix's built-in restore-on-close only knows how to refocus its own
 * `<Dialog.Trigger>`, and none of these callers render one — `useConfirm()` is
 * imperative/headless, and the per-open modals are mounted by their parent's state, not a
 * trigger element. Focus TRAP and Escape-to-cancel stay Radix's job (`FocusScope` +
 * `DismissableLayer`); restoration is the one piece wired here.
 *
 * The `"on-mount"` capture is guarded by an `undefined` sentinel so it runs at most once per
 * mount, never re-read on a later render — by then `document.activeElement` is already inside
 * the dialog, worthless as a restore target.
 */
export function useRestoreFocus(timing: RestoreFocusTiming): UseRestoreFocusResult {
  // `undefined` = not captured yet; `null` = captured, but nothing focusable held focus. Both
  // restore nothing on close (`?.focus()`), so `"imperative"` closing before any capture — which
  // can't happen through `useConfirm()`, but costs nothing to tolerate — is safe.
  const restoreFocusRef = useRef<HTMLElement | null | undefined>(undefined);

  if (timing === "on-mount" && restoreFocusRef.current === undefined) {
    restoreFocusRef.current = captureActiveElement();
  }

  const capture = useCallback((): void => {
    restoreFocusRef.current = captureActiveElement();
  }, []);

  const onCloseAutoFocus = useCallback((event: Event): void => {
    event.preventDefault();
    restoreFocusRef.current?.focus();
  }, []);

  // A stable result object, not a fresh literal per render — `ConfirmDialogProvider` threads this
  // into its `confirm` useCallback's deps, and an identity churn here would re-create `confirm`
  // (and its context value) on every provider render, exactly what that provider's pendingRef
  // dance exists to avoid.
  return useMemo(() => ({ capture, onCloseAutoFocus }), [capture, onCloseAutoFocus]);
}
