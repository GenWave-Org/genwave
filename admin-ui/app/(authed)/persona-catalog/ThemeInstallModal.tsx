"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { readErrorMessage } from "@/lib/problem-details";
import { prettifySlug } from "./format-slug";

export interface ThemeInstallResult {
  name: string;
  /** The provenance stamp the import route actually wrote (SPEC F103.11) — always this modal's own
   * `slug` in practice (see `ThemeCatalogProvenanceDto`'s own remarks), read off the response
   * rather than assumed, mirroring `ThemeImportSuccessBody`'s own already-present field. */
  importedFrom: string;
  /** When {@link importedFrom} was stamped (gh-#375) — a server read-back
   * (`ThemesImportController`'s own remarks), never a client-side `Date.now()` guess, so
   * `PersonaCatalogClient`'s post-install local flip can show the SAME provenance line a fresh
   * `GET /api/settings` read would. */
  importedAt: string;
}

interface ThemeImportSuccessBody {
  slug: string;
  name: string;
  importedFrom: string;
  importedAt: string;
}

export interface ThemeInstallModalProps {
  /** The catalog entry's own slug — used as BOTH the install route's target slug and the
   * `?catalogSlug=` provenance value (SPEC F90.7's persona precedent, applied to the theme kind by
   * PLAN T186: a catalog theme installs under the same slug it is known by on the shelf). */
  slug: string;
  /** The raw, already hash-verified theme manifest JSON text (SPEC F90.3) — the SAME bytes
   * `ThemeDetailPreview` already composed a preview from; POSTed byte-for-byte on confirm, never
   * re-derived or re-fetched. */
  manifestText: string;
  onCancel: () => void;
  onInstalled: (result: ThemeInstallResult) => void;
}

type ConfirmStatus = { kind: "idle" } | { kind: "installing" } | { kind: "error"; message: string };

/**
 * The theme catalog's install confirmation (SPEC F103.6, STORY-274, PLAN T186) — the trust ruling's
 * "review, then explicitly confirm" stop (ARCHITECTURE.md "Trust ruling"), applied to the theme
 * kind: opening this dialog issues no request of any kind; only Confirm does, and Cancel/Escape/a
 * backdrop click all close it with none either. A theme's "review" is the live composed preview
 * already showing behind this modal (`ThemeDetailPreview`) — unlike `PersonaCardReviewModal`, this
 * dialog does not re-render the manifest's own fields, it only asks for the final go/no-go.
 *
 * House modal conventions mirrored from `PersonaCardReviewModal` (Radix `Dialog`; Cancel, Escape,
 * and a backdrop click all route through the same `onOpenChange` → `onCancel` path; hand-wired
 * focus restoration since this component mounts fresh with no real `Dialog.Trigger` of its own —
 * see that component's own remarks for the full reasoning).
 */
export function ThemeInstallModal({ slug, manifestText, onCancel, onInstalled }: ThemeInstallModalProps): ReactNode {
  const [status, setStatus] = useState<ConfirmStatus>({ kind: "idle" });

  const restoreFocusRef = useRef<HTMLElement | null | undefined>(undefined);
  if (restoreFocusRef.current === undefined) {
    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  }

  async function handleConfirm(): Promise<void> {
    if (status.kind === "installing") return;
    setStatus({ kind: "installing" });

    const encodedSlug = encodeURIComponent(slug);

    try {
      const resp = await fetch(`/api/themes/${encodedSlug}/import?catalogSlug=${encodedSlug}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: manifestText,
      });

      if (resp.ok) {
        const body = (await resp.json()) as ThemeImportSuccessBody;
        onInstalled({ name: body.name, importedFrom: body.importedFrom, importedAt: body.importedAt });
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
          data-testid="theme-install-overlay"
          className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none"
        />
        <Dialog.Content
          aria-label="Install theme"
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
            The station adopts this theme immediately for anyone who selects it. Nothing installs until
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
