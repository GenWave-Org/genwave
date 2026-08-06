"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { readErrorMessage } from "@/lib/problem-details";
import { prettifySlug } from "./format-slug";

export interface FontInstallResult {
  family: string;
}

interface FontPackInstallSuccessBody {
  slug: string;
  family: string;
  faces: string[];
  importedFrom: string;
}

export interface FontInstallModalProps {
  /** The catalog entry's own slug — the install route's target AND upsert key
   * (`FontPackController.Install`, SPEC F104.5): a pack installs under the same slug it is known
   * by on the shelf. No separate provenance parameter (unlike `ThemeInstallModal`'s own
   * `?catalogSlug=`) — a font pack has no other install path a provenance stamp would need to
   * disambiguate from (SPEC F104.5: "packs have no file-upload or authored path"). */
  slug: string;
  onCancel: () => void;
  onInstalled: (result: FontInstallResult) => void;
}

type ConfirmStatus = { kind: "idle" } | { kind: "installing" } | { kind: "error"; message: string };

/**
 * The font catalog's install confirmation (SPEC F104.5, STORY-282, PLAN T202 — a scope addition,
 * see `FontDetailPanel`'s own remarks for why this task builds it) — the trust ruling's "review,
 * then explicitly confirm" stop applied to the font kind, mirroring `ThemeInstallModal`'s own
 * confirm/cancel semantics exactly: opening this dialog issues no request of any kind; only
 * Confirm does, and Cancel/Escape/a backdrop click all close it with none either.
 *
 * <b>No request body</b> (unlike `ThemeInstallModal`'s POSTed manifest text): `FontPackController.Install`
 * takes ONLY the route slug and fetches every byte itself, server-side, through the guarded door
 * (SPEC F104.5's own "no request body, by design" rule) — this dialog's Confirm button POSTs with
 * no body at all. The "review" already happened via `SpecimenBlock`'s own real, hash-verified face
 * showing behind this modal, not a manifest this dialog would otherwise have to re-echo.
 */
export function FontInstallModal({ slug, onCancel, onInstalled }: FontInstallModalProps): ReactNode {
  const [status, setStatus] = useState<ConfirmStatus>({ kind: "idle" });

  const restoreFocusRef = useRef<HTMLElement | null | undefined>(undefined);
  if (restoreFocusRef.current === undefined) {
    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  }

  async function handleConfirm(): Promise<void> {
    if (status.kind === "installing") return;
    setStatus({ kind: "installing" });

    try {
      const resp = await fetch(`/api/fonts/${encodeURIComponent(slug)}/install`, { method: "POST" });

      if (resp.ok) {
        const body = (await resp.json()) as FontPackInstallSuccessBody;
        onInstalled({ family: body.family });
        return;
      }

      setStatus({ kind: "error", message: await readErrorMessage(resp) });
    } catch {
      setStatus({ kind: "error", message: "Network error — check your connection" });
    }
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
          data-testid="font-install-overlay"
          className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none"
        />
        <Dialog.Content
          aria-label="Install font pack"
          className="fixed left-1/2 top-1/2 z-50 flex w-[calc(100%-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 flex-col rounded-[6px] border border-line bg-surface p-6 transition-opacity duration-200 ease-out focus:outline-none motion-reduce:transition-none"
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            restoreFocusRef.current?.focus();
          }}
        >
          <Dialog.Title className="font-display text-[1.1rem] text-ink">
            Install &quot;{prettifySlug(slug)}&quot;?
          </Dialog.Title>
          <Dialog.Description className="mt-1 text-[0.82rem] text-mute">
            The station fetches and stores this pack&apos;s faces immediately. Nothing installs until
            you confirm.
          </Dialog.Description>

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
