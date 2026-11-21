"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { requestTtsPreview, useTtsPreviewPlayer } from "@/lib/use-tts-preview";

/** What to preview: a saved persona by id, or the create/edit form's in-progress (possibly
 * unsaved) fields straight off the operator's draft (SPEC F35.6/F35.7 — preview also works for a
 * draft that has never been POSTed). */
export type PersonaPreviewTarget =
  | { kind: "saved"; personaId: number; voice: string }
  | { kind: "draft"; name: string; backstory: string; style: string; voice: string };

interface PersonaPreviewProps {
  target: PersonaPreviewTarget;
}

interface ProblemDetailsBody {
  title?: string;
  detail?: string;
}

function isProblemDetailsBody(raw: unknown): raw is ProblemDetailsBody {
  return typeof raw === "object" && raw !== null;
}

/**
 * Reads the ProblemDetails `title` first, falling back to `detail`, then a generic message.
 * Preview failures toast the *title* (SPEC F35.7: "toast with the ProblemDetails title") — this
 * is deliberately title-first, unlike the detail-first convention the CRUD mutations in
 * PersonasClient use, because `PersonaController.Preview`'s 502 title ("Persona preview failed.")
 * is the honest, human-facing outcome; the detail is server-log-oriented LLM-failure detail.
 */
async function readPreviewFailureMessage(resp: Response): Promise<string> {
  try {
    const raw = (await resp.json()) as unknown;
    if (isProblemDetailsBody(raw)) {
      if (typeof raw.title === "string" && raw.title !== "") return raw.title;
      if (typeof raw.detail === "string" && raw.detail !== "") return raw.detail;
    }
  } catch {
    // malformed/empty body — fall through to the generic message
  }
  return `Unexpected error (${resp.status})`;
}

type CopyState = { kind: "idle" } | { kind: "loading" } | { kind: "loaded"; text: string };

/** Builds the POST /api/personas/preview body for a target (SPEC F35.6). No `kind`/`mediaId` —
 * this page never surfaces a kind or track picker; the endpoint's `LeadIn` default and legal
 * null-track preview cover it. `voice` is omitted when blank, mirroring the same
 * omit-if-station-default convention the Generate form's wire body already uses. */
function previewRequestBody(target: PersonaPreviewTarget): Record<string, unknown> {
  if (target.kind === "saved") {
    return { personaId: target.personaId };
  }
  const body: Record<string, unknown> = {
    name: target.name,
    backstory: target.backstory,
    style: target.style,
  };
  if (target.voice.trim() !== "") body["voice"] = target.voice;
  return body;
}

/**
 * Persona copy preview + in-page wav playback (SPEC F35.6/F35.7, STORY-126). Mounted once per
 * table row (auditions the saved persona) and once inside the create/edit form (auditions
 * whatever is currently typed, saved or not). Both drive the same two calls: POST
 * /api/personas/preview for the copy text — LLM-only, no template fallback, so a 502 toasts
 * honestly and renders nothing, never a substituted template passed off as the persona's own
 * words (F35.6) — then, once the operator asks to hear it, POST /api/tts/preview for a
 * synchronous wav played back via a blob URL.
 */
export function PersonaPreview({ target }: PersonaPreviewProps): ReactNode {
  const [copy, setCopy] = useState<CopyState>({ kind: "idle" });
  const player = useTtsPreviewPlayer();

  async function handlePreview(): Promise<void> {
    setCopy({ kind: "loading" });
    player.clear();

    try {
      const resp = await fetch("/api/personas/preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(previewRequestBody(target)),
      });

      if (resp.ok) {
        const data = (await resp.json()) as { text: string };
        setCopy({ kind: "loaded", text: data.text });
        return;
      }

      setCopy({ kind: "idle" });
      toast.error(await readPreviewFailureMessage(resp));
    } catch {
      setCopy({ kind: "idle" });
      toast.error("Network error — check your connection");
    }
  }

  async function handlePlay(): Promise<void> {
    if (copy.kind !== "loaded") return;

    const voice = target.voice.trim() === "" ? undefined : target.voice;
    await player.hear(() => requestTtsPreview({ text: copy.text, voice }, readPreviewFailureMessage));
  }

  return (
    <div className="flex flex-col gap-2">
      <Button
        type="button"
        variant="secondary"
        onClick={() => {
          void handlePreview();
        }}
        disabled={copy.kind === "loading"}
      >
        {copy.kind === "loading" ? "Writing…" : "Preview"}
      </Button>

      {copy.kind === "loaded" && (
        <div className="rounded-[6px] border border-line bg-surface-2 p-3 text-[0.85rem] text-ink">
          <p>{copy.text}</p>
          <div className="mt-2 flex items-center gap-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => {
                void handlePlay();
              }}
              disabled={player.isRendering}
            >
              {player.isRendering ? "Rendering…" : "Play"}
            </Button>
            {player.url !== null && <audio controls src={player.url} aria-label="Persona preview audio" />}
          </div>
        </div>
      )}
    </div>
  );
}
