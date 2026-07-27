"use client";

import { useRef, useState, type ChangeEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { PersonaCardReviewModal, type PersonaCardReviewImportResult } from "../_components/PersonaCardReviewModal";

export interface PersonaImportPanelProps {
  /** Called after a successful import so the parent can refresh its persona list — F79.3's
   * upsert-by-slug may have created a new row or updated an existing one, and the import
   * response itself carries no full `PersonaDto` (no backstory/style) to splice in locally. */
  onImported: () => void;
}

/** Mirrors `PersonaController.MaxImportBytes` (SPEC F79.6). Advisory only — the server enforces
 * its own cap regardless of what this client-side check lets through. */
const MAX_IMPORT_BYTES = 256 * 1024;

type ImportStatus =
  | { kind: "idle" }
  | { kind: "oversized"; fileName: string; sizeBytes: number }
  | { kind: "unreadable"; fileName: string }
  | { kind: "reviewing"; text: string }
  | { kind: "done"; name: string; created: boolean; warnings: string[] };

/** `FileReader` rather than `Blob.prototype.text()` — broadly supported in every real browser
 * and, unlike `.text()`, also works against this project's own jsdom test environment. */
function readFileAsText(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error ?? new Error("Failed to read file"));
    reader.readAsText(file);
  });
}

/**
 * Import flow (SPEC F90.6, STORY-236, PLAN T104): file picker → read the file's text → the SAME
 * `PersonaCardReviewModal` T103 already built for the catalog door (STORY-235) → confirm → the
 * existing `POST /api/personas/{slug}/import`, WITHOUT a `catalogSlug` (the T103 no-slug seam
 * that modal already exposes) — the server stamps `imported_from = "file"` on that path by
 * default (F90.7/T98). The trust ruling recognizes exactly one adoption gate, not "which button
 * started it": there is no second preview pane here anymore (this used to render its own
 * name/tagline/voice/quirk+lore+taste summary via `parsePersonaCardPreview` — superseded, see
 * below), and no import request of any kind before the operator clicks Confirm inside that modal.
 * Success stays on `/personas` — this panel already lives there, so `onImported` just tells the
 * parent to refresh its list, the same as it always has.
 *
 * Two client-side guards run BEFORE the modal ever opens, because neither is something the modal
 * could render usefully — no card text exists yet to review:
 *  - oversized: checked against the file's raw byte size (`file.size`), before any read is even
 *    attempted, mirroring the server's own size-before-deserialization gate order
 *    (`PersonaController.Import` remarks). Unchanged from this panel's pre-T104 shape.
 *  - unreadable: a genuine I/O failure from `FileReader` itself — not a JSON/schema problem, so
 *    there's no card text to hand the modal at all.
 *
 * A file that reads fine but ISN'T valid JSON (or is missing a usable `name`) is deliberately NOT
 * special-cased here — it still opens the modal, which already renders its own "couldn't be read"
 * error state with Confirm disabled (see `persona-card-review-modal.spec.tsx`'s malformed-card
 * scenario). Reusing that state rather than re-implementing a second malformed-JSON check in this
 * panel is the point of "one adoption rule, no loophole" (T104): this panel no longer parses the
 * card at all — `parsePersonaCardPreview`/`persona-card.ts` remains in the tree only for
 * `use-persona-voice-warning.ts`'s unrelated read-back-an-existing-persona's-card use — it only
 * reads bytes and hands them to the one component that both previews AND imports them.
 */
export function PersonaImportPanel({ onImported }: PersonaImportPanelProps): ReactNode {
  const [status, setStatus] = useState<ImportStatus>({ kind: "idle" });
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  function reset(): void {
    setStatus({ kind: "idle" });
    if (fileInputRef.current !== null) fileInputRef.current.value = "";
  }

  async function handleFileChange(e: ChangeEvent<HTMLInputElement>): Promise<void> {
    const file = e.currentTarget.files?.[0];
    if (file === undefined) return;

    // Checked BEFORE reading the file's text — an honest "too large" message client-side, mirroring
    // the server's own size-before-deserialization gate order (PersonaController.Import remarks).
    if (file.size > MAX_IMPORT_BYTES) {
      setStatus({ kind: "oversized", fileName: file.name, sizeBytes: file.size });
      return;
    }

    try {
      const text = await readFileAsText(file);
      setStatus({ kind: "reviewing", text });
    } catch {
      setStatus({ kind: "unreadable", fileName: file.name });
    }
  }

  /** Cancel abandons the whole attempt (STORY-236 AC1: cancel = no state change) — back to
   * `idle` with the file input cleared, the same full reset this panel's own Cancel button has
   * always done for every other exit, so re-selecting the identical file name still fires a fresh
   * `change` event rather than a no-op. */
  function handleModalCancel(): void {
    reset();
  }

  function handleModalImported(result: PersonaCardReviewImportResult): void {
    setStatus({ kind: "done", name: result.name, created: result.created, warnings: result.warnings });
    if (fileInputRef.current !== null) fileInputRef.current.value = "";
    toast.success(`"${result.name}" ${result.created ? "imported" : "updated"}.`);
    onImported();
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-1.5">
        <label htmlFor="persona-import-file" className="text-[0.82rem] font-semibold text-mute">
          Persona card (.json)
        </label>
        <input
          id="persona-import-file"
          ref={fileInputRef}
          type="file"
          accept=".json,application/json"
          disabled={status.kind === "reviewing"}
          onChange={(e) => {
            void handleFileChange(e);
          }}
          className="text-[0.85rem] text-ink disabled:opacity-50"
        />
      </div>

      {status.kind === "oversized" && (
        <p role="alert" className="text-[0.82rem] text-danger">
          &quot;{status.fileName}&quot; is {Math.ceil(status.sizeBytes / 1024)} KB — over the{" "}
          {MAX_IMPORT_BYTES / 1024} KB limit. Choose a smaller file.
        </p>
      )}

      {status.kind === "unreadable" && (
        <p role="alert" className="text-[0.82rem] text-danger">
          &quot;{status.fileName}&quot; couldn&apos;t be read. Choose the file again.
        </p>
      )}

      {status.kind === "reviewing" && (
        <PersonaCardReviewModal cardText={status.text} onCancel={handleModalCancel} onImported={handleModalImported} />
      )}

      {status.kind === "done" && (
        <div className="rounded-[6px] border border-line bg-surface-2 p-3 text-[0.85rem] text-ink">
          <p>
            &quot;{status.name}&quot; {status.created ? "imported" : "updated"}.
          </p>
          {status.warnings.length > 0 && (
            <ul className="mt-2 flex flex-col gap-1 text-danger">
              {status.warnings.map((warning) => (
                <li key={warning} role="alert">
                  {warning}
                </li>
              ))}
            </ul>
          )}
          <Button type="button" variant="secondary" className="mt-3" onClick={reset}>
            Import another
          </Button>
        </div>
      )}
    </div>
  );
}
