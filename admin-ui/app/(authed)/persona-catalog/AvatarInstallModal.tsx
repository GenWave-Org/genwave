"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { readErrorMessage } from "@/lib/problem-details";
import { prettifySlug } from "./format-slug";

export interface AvatarInstallResult {
  packName: string;
}

interface AvatarPackInstallSuccessBody {
  slug: string;
  packName: string;
  items: string[];
  importedFrom: string;
}

export interface AvatarInstallModalProps {
  /** The catalog entry's own slug — the install route's target AND upsert key
   * (`AvatarPackController.Install`, SPEC F128.3): a pack installs under the same slug it is known
   * by on the shelf, mirroring `FontInstallModal`'s own `slug` prop. */
  slug: string;
  onCancel: () => void;
  onInstalled: (result: AvatarInstallResult) => void;
}

type ConfirmStatus = { kind: "idle" } | { kind: "installing" } | { kind: "error"; message: string };

/**
 * The avatar pack catalog's install confirmation (SPEC F128.3, STORY-332, PLAN T294) — mirrors
 * `FontInstallModal`'s own confirm/cancel semantics exactly (this task's own instruction: "match
 * however FONT pack install confirms — read the font shelf flow and mirror its confirm/trust
 * treatment exactly"). Opening this dialog issues no request of any kind; only Confirm does, and
 * Cancel/Escape/a backdrop click all close it with none either. `AvatarPackController.Install` takes
 * no request body — every byte is fetched, re-validated, and normalized server-side through the
 * guarded door (that controller's own remarks) — so Confirm POSTs with no body at all, the SAME
 * shape `FontInstallModal`'s own Confirm button already uses. The "review" already happened via
 * `AvatarDetailPanel`'s own face grid, real hash-verified previews shown behind this modal, not a
 * manifest this dialog would otherwise have to re-echo.
 */
export function AvatarInstallModal({ slug, onCancel, onInstalled }: AvatarInstallModalProps): ReactNode {
  const [status, setStatus] = useState<ConfirmStatus>({ kind: "idle" });

  const restoreFocusRef = useRef<HTMLElement | null | undefined>(undefined);
  if (restoreFocusRef.current === undefined) {
    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  }

  async function handleConfirm(): Promise<void> {
    if (status.kind === "installing") return;
    setStatus({ kind: "installing" });

    try {
      const resp = await fetch(`/api/avatar-packs/${encodeURIComponent(slug)}/install`, { method: "POST" });

      if (resp.ok) {
        const body = (await resp.json()) as AvatarPackInstallSuccessBody;
        onInstalled({ packName: body.packName });
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
          data-testid="avatar-install-overlay"
          className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none"
        />
        <Dialog.Content
          aria-label="Install avatar pack"
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
            The station fetches, re-validates, and stores this pack&apos;s faces immediately. Nothing
            installs until you confirm.
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
