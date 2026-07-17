"use client";

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import * as Dialog from "@radix-ui/react-dialog";
import { Button } from "@/components/ui/button";

// Wireless confirm-dialog conventions (.claude/skills/design-aesthetic,
// SPEC F28.9/F28.14): the browser's native confirm prompt is removed in
// favor of a modal that states the consequence in plain words. Focus trap
// and Escape-to-cancel are Radix Dialog's job (FocusScope + DismissableLayer)
// — we don't hand-roll either. Focus *restoration* is the one piece we wire
// ourselves: Radix's built-in restore-on-close only knows how to refocus its
// own <Dialog.Trigger>, and useConfirm() is imperative/headless — callers
// never render one. So we capture whatever had focus when confirm() was
// called and restore it explicitly via onCloseAutoFocus.

export interface ConfirmOptions {
  title: string;
  /** Plain-words statement of what confirming does — no jargon, no euphemism. */
  consequence: string;
  confirmLabel?: string;
  cancelLabel?: string;
  /** Renders the confirm button as `variant="destructive"` (solid --danger). */
  destructive?: boolean;
}

type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>;

const ConfirmContext = createContext<ConfirmFn | null>(null);

interface PendingConfirm {
  options: ConfirmOptions;
  resolve: (result: boolean) => void;
}

interface ConfirmDialogProviderProps {
  children: ReactNode;
}

/**
 * Mounts the one confirm-dialog instance for the whole authed shell and
 * exposes `useConfirm()` to any descendant. Only one confirmation can be
 * pending at a time, which matches every call site (a confirm always blocks
 * the action that requested it).
 */
export function ConfirmDialogProvider({ children }: ConfirmDialogProviderProps): ReactNode {
  const [pending, setPending] = useState<PendingConfirm | null>(null);
  // Mirrors `pending` so `settle` can resolve the in-flight promise without
  // taking a dependency on the state value (and re-creating on every render).
  const pendingRef = useRef<PendingConfirm | null>(null);
  // What had focus right before confirm() opened the dialog — restored
  // explicitly on close (see file header comment).
  const restoreFocusRef = useRef<HTMLElement | null>(null);

  const settle = useCallback((result: boolean) => {
    pendingRef.current?.resolve(result);
    pendingRef.current = null;
    setPending(null);
  }, []);

  const confirm = useCallback<ConfirmFn>((options) => {
    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    return new Promise<boolean>((resolve) => {
      const entry: PendingConfirm = { options, resolve };
      pendingRef.current = entry;
      setPending(entry);
    });
  }, []);

  const handleOpenChange = useCallback(
    (open: boolean) => {
      // Fires on Escape, overlay click, and our own explicit Cancel/Confirm
      // handlers (via Dialog.Content's onOpenChange after settle() already
      // resolved — a second resolve() call on a settled Promise is a no-op).
      if (!open) settle(false);
    },
    [settle]
  );

  const contextValue = useMemo(() => confirm, [confirm]);

  return (
    <ConfirmContext.Provider value={contextValue}>
      {children}
      <Dialog.Root open={pending !== null} onOpenChange={handleOpenChange}>
        <Dialog.Portal>
          <Dialog.Overlay className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none" />
          <Dialog.Content
            className="fixed left-1/2 top-1/2 z-50 w-[calc(100%-2rem)] max-w-sm -translate-x-1/2 -translate-y-1/2 rounded-[6px] border border-line bg-surface p-6 transition-opacity duration-200 ease-out focus:outline-none motion-reduce:transition-none"
            onCloseAutoFocus={(event) => {
              event.preventDefault();
              restoreFocusRef.current?.focus();
            }}
          >
            {pending && (
              <>
                <Dialog.Title className="font-display text-[1.1rem] text-ink">
                  {pending.options.title}
                </Dialog.Title>
                <Dialog.Description className="mt-2 text-[0.85rem] text-mute">
                  {pending.options.consequence}
                </Dialog.Description>
                <div className="mt-6 flex justify-end gap-2">
                  <Button variant="secondary" onClick={() => settle(false)}>
                    {pending.options.cancelLabel ?? "Cancel"}
                  </Button>
                  {/* No autoFocus here: Radix's FocusScope owns initial-focus
                      placement on mount (and captures the pre-open activeElement
                      to restore on close) — a React autoFocus prop would fire
                      during commit, before FocusScope's effect runs, and get
                      mistaken by FocusScope for the caller's own focus target. */}
                  <Button
                    variant={pending.options.destructive ? "destructive" : "primary"}
                    onClick={() => settle(true)}
                  >
                    {pending.options.confirmLabel ?? "Confirm"}
                  </Button>
                </div>
              </>
            )}
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </ConfirmContext.Provider>
  );
}

/** Returns a `confirm()` function: `await confirm({ title, consequence })` resolves `true`/`false`. */
export function useConfirm(): ConfirmFn {
  const ctx = useContext(ConfirmContext);
  if (ctx === null) {
    throw new Error("useConfirm() must be called within a ConfirmDialogProvider");
  }
  return ctx;
}
