"use client";

import { useRef, useState, type ChangeEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { parsePersonaCardPreview, type PersonaCardPreview } from "./persona-card";
import { readErrorMessage } from "./persona-http";
import { personaSlug } from "./persona-slug";

export interface PersonaImportPanelProps {
  /** Called after a successful import so the parent can refresh its persona list — F79.3's
   * upsert-by-slug may have created a new row or updated an existing one, and the import
   * response itself carries no full `PersonaDto` (no backstory/style) to splice in locally. */
  onImported: () => void;
}

/** Mirrors `PersonaController.MaxImportBytes` (SPEC F79.6). Advisory only — the server enforces
 * its own cap regardless of what this client-side check lets through. */
const MAX_IMPORT_BYTES = 256 * 1024;

/** Response shape of a successful `POST /api/personas/{slug}/import` (`PersonaImportResponse`). */
interface ImportSuccessBody {
  name: string;
  warnings: string[];
}

type ImportStatus =
  | { kind: "idle" }
  | { kind: "oversized"; fileName: string; sizeBytes: number }
  | { kind: "picked"; fileName: string; text: string; preview: PersonaCardPreview | null }
  | { kind: "importing"; fileName: string; text: string; preview: PersonaCardPreview | null }
  | { kind: "done"; name: string; created: boolean; warnings: string[] }
  | { kind: "error"; fileName: string; text: string; preview: PersonaCardPreview | null; message: string };

/**
 * Best-effort slug for the import target (SPEC F79.3's `{slug}/import` addressing): prefers the
 * card's own `name` field, run through the SAME `personaSlug` the Export link and the backend's
 * `Slugify` both use, so it reproduces exactly the slug the ORIGIN station assigned — falling
 * back to the uploaded file's own name only when the card failed to parse at all (nothing else
 * to derive a slug from).
 */
function importSlug(fileName: string, preview: PersonaCardPreview | null): string {
  if (preview !== null) return personaSlug(preview.name);
  const stem = fileName.replace(/\.persona\.json$/i, "").replace(/\.json$/i, "");
  return personaSlug(stem);
}

function describeVoice(voiceId: string): string {
  return voiceId === "" ? "Station default" : voiceId;
}

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
 * Import flow (SPEC F79.4–F79.6, STORY-209, PLAN T68): file picker → client-side preview
 * (name/tagline/voice/quirk+lore+taste counts, NEVER authoritative) → confirm →
 * `POST /api/personas/{slug}/import` with the file's ORIGINAL bytes — never a client
 * re-serialization of the parsed preview, which would silently drop any field the preview's
 * narrower parse doesn't know about (`corrections`, `energyDisposition`, `schemaVersion`, …).
 * Success shows created/updated plus any F79.4 voice warnings, kept visible in this panel (not
 * just a toast, which auto-dismisses) until the operator starts another import. Every rejection
 * (400/409/413) surfaces the server's own ProblemDetails `detail` verbatim, including the
 * newer-major message naming both schema versions (F79.2) and the oversized message (F79.6).
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

    const text = await readFileAsText(file);
    setStatus({ kind: "picked", fileName: file.name, text, preview: parsePersonaCardPreview(text) });
  }

  async function handleConfirm(): Promise<void> {
    if (status.kind !== "picked" && status.kind !== "error") return;
    const { fileName, text, preview } = status;
    setStatus({ kind: "importing", fileName, text, preview });

    const slug = importSlug(fileName, preview);

    try {
      const resp = await fetch(`/api/personas/${slug}/import`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: text,
      });

      if (resp.status === 201 || resp.status === 200) {
        const body = (await resp.json()) as ImportSuccessBody;
        const created = resp.status === 201;
        setStatus({ kind: "done", name: body.name, created, warnings: body.warnings });
        toast.success(`"${body.name}" ${created ? "imported" : "updated"}.`);
        onImported();
        return;
      }

      const message = await readErrorMessage(resp);
      setStatus({ kind: "error", fileName, text, preview, message });
      toast.error(message);
    } catch {
      const message = "Network error — check your connection";
      setStatus({ kind: "error", fileName, text, preview, message });
      toast.error(message);
    }
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
          disabled={status.kind === "importing"}
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

      {(status.kind === "picked" || status.kind === "importing" || status.kind === "error") && (
        <div
          role="region"
          aria-label="Persona card preview"
          className="rounded-[6px] border border-line bg-surface-2 p-3 text-[0.85rem] text-ink"
        >
          {status.preview === null ? (
            <p className="text-mute">
              Couldn&apos;t preview &quot;{status.fileName}&quot; — it may still import successfully;
              the server validates it either way.
            </p>
          ) : (
            <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
              <dt className="text-mute">Name</dt>
              <dd>{status.preview.name}</dd>
              <dt className="text-mute">Tagline</dt>
              <dd>{status.preview.tagline === "" ? "—" : status.preview.tagline}</dd>
              <dt className="text-mute">Voice</dt>
              <dd>{describeVoice(status.preview.voiceId)}</dd>
              <dt className="text-mute">Quirks</dt>
              <dd>{status.preview.quirkCount}</dd>
              <dt className="text-mute">Lore</dt>
              <dd>{status.preview.loreCount}</dd>
              <dt className="text-mute">Taste rules</dt>
              <dd>{status.preview.tasteCount}</dd>
            </dl>
          )}

          {status.kind === "error" && (
            <p role="alert" className="mt-2 text-danger">
              {status.message}
            </p>
          )}

          <div className="mt-3 flex gap-2">
            <Button
              type="button"
              onClick={() => {
                void handleConfirm();
              }}
              disabled={status.kind === "importing"}
            >
              {status.kind === "importing" ? "Importing…" : "Confirm import"}
            </Button>
            <Button type="button" variant="secondary" onClick={reset} disabled={status.kind === "importing"}>
              Cancel
            </Button>
          </div>
        </div>
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
