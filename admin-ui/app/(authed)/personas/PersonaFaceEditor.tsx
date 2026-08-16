"use client";

import { useRef, useState, type ChangeEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { readErrorMessage } from "@/lib/problem-details";
import { PersonaFace } from "./PersonaFace";

export interface PersonaFaceEditorProps {
  personaId: number;
  personaName: string;
  /** `PersonasClient`'s own per-persona cache-bust counter — see `PersonaFace`'s own remarks. */
  version: number;
  /** Bumps `version` in the parent after ANY successful write below (upload or remove) — the SAME
   * write that just resolved also feeds the roster row's own `PersonaFace`, one level up, so both
   * the editor's portrait and the row's thumbnail go stale and fresh together. */
  onChanged: () => void;
}

/** Advisory client-side mirror of `ImageNormalizeService.MaxInputBytes` (Host) — the server enforces
 * its own ceiling regardless of what this check lets through; this only spares an operator the
 * round trip for an obviously-oversized file, the SAME "advisory only" posture
 * `PersonaImportPanel.MAX_IMPORT_BYTES` already documents for its own file input. */
const MAX_UPLOAD_BYTES = 4 * 1024 * 1024;

/**
 * Upload/remove controls for a persona's worn face (SPEC F128.5/.6, STORY-333, PLAN T296) — sits
 * inside the persona editor (the "detail" half of this page's card/detail pair; see
 * `PersonasClient`'s own remarks for why this admin UI has no separate detail ROUTE). PUTs the
 * chosen file's raw bytes straight to the write route Host's `PersonaAvatarController` already
 * ships (T295) — no client-side re-encode, no FormData wrapper: the route's own `[Consumes]`-free
 * shape (Content-Type is advisory only there too) accepts a bare binary body. Errors surface the
 * server's own honest, per-reason `ProblemDetails.Detail` (over-ceiling reads distinctly from a
 * decode failure — PersonaAvatarController's own NormalizeFailureProblem), never a generic
 * "upload failed" — the SAME `readErrorMessage` detail-first convention every other mutation on
 * this page already follows.
 */
export function PersonaFaceEditor({ personaId, personaName, version, onChanged }: PersonaFaceEditorProps): ReactNode {
  const [isBusy, setIsBusy] = useState(false);
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  async function handleFileChange(e: ChangeEvent<HTMLInputElement>): Promise<void> {
    const file = e.currentTarget.files?.[0];
    if (file === undefined) return;

    if (file.size > MAX_UPLOAD_BYTES) {
      toast.error(`"${file.name}" is over the ${MAX_UPLOAD_BYTES / (1024 * 1024)} MiB limit.`);
      if (fileInputRef.current !== null) fileInputRef.current.value = "";
      return;
    }

    setIsBusy(true);
    try {
      const resp = await fetch(`/api/personas/${personaId}/avatar`, {
        method: "PUT",
        headers: { "Content-Type": file.type || "application/octet-stream" },
        body: file,
      });
      if (resp.ok) {
        toast.success(`Face updated for "${personaName}".`);
        onChanged();
      } else {
        toast.error(await readErrorMessage(resp));
      }
    } catch {
      toast.error("Network error — check your connection");
    }
    if (fileInputRef.current !== null) fileInputRef.current.value = "";
    setIsBusy(false);
  }

  async function handleRemove(): Promise<void> {
    setIsBusy(true);
    try {
      const resp = await fetch(`/api/personas/${personaId}/avatar`, { method: "DELETE" });
      if (resp.status === 204) {
        toast.success(`Face removed for "${personaName}".`);
        onChanged();
      } else {
        toast.error(await readErrorMessage(resp));
      }
    } catch {
      toast.error("Network error — check your connection");
    }
    setIsBusy(false);
  }

  return (
    <div className="flex flex-col gap-2">
      <span className={FIELD_LABEL_CLASSES}>Face</span>
      <div className="flex items-center gap-3">
        <PersonaFace personaId={personaId} personaName={personaName} version={version} size="lg" />
        <div className="flex flex-col gap-1.5">
          <label htmlFor={`persona-face-file-${personaId}`} className="sr-only">
            Upload a face for {personaName}
          </label>
          <input
            id={`persona-face-file-${personaId}`}
            ref={fileInputRef}
            type="file"
            accept="image/png,image/jpeg"
            disabled={isBusy}
            onChange={(e) => {
              void handleFileChange(e);
            }}
            className="text-[0.82rem] text-ink disabled:opacity-50"
          />
          <Button
            type="button"
            variant="secondary"
            aria-label={`Remove ${personaName}'s face`}
            disabled={isBusy}
            onClick={() => {
              void handleRemove();
            }}
            className="w-fit"
          >
            Remove face
          </Button>
        </div>
      </div>
    </div>
  );
}

const FIELD_LABEL_CLASSES = "text-[0.82rem] font-semibold text-mute";
