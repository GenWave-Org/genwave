"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";

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
type PlaybackState = { kind: "idle" } | { kind: "loading" } | { kind: "ready"; url: string };

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
  const [playback, setPlayback] = useState<PlaybackState>({ kind: "idle" });
  const audioUrlRef = useRef<string | null>(null);

  // Revoke whatever blob URL this instance is holding on unmount so a closed preview panel
  // doesn't leak a blob for the lifetime of the page.
  useEffect(
    () => () => {
      if (audioUrlRef.current !== null) URL.revokeObjectURL(audioUrlRef.current);
    },
    []
  );

  async function handlePreview(): Promise<void> {
    setCopy({ kind: "loading" });
    if (audioUrlRef.current !== null) {
      URL.revokeObjectURL(audioUrlRef.current);
      audioUrlRef.current = null;
    }
    setPlayback({ kind: "idle" });

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

    setPlayback({ kind: "loading" });
    // A re-render ("Play" clicked again for a fresh render) would otherwise overwrite this ref
    // with a new blob URL, leaking the previous one until unmount — revoke it first.
    if (audioUrlRef.current !== null) {
      URL.revokeObjectURL(audioUrlRef.current);
      audioUrlRef.current = null;
    }
    const voice = target.voice.trim() === "" ? undefined : target.voice;

    try {
      const resp = await fetch("/api/tts/preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ text: copy.text, voice }),
      });

      if (!resp.ok) {
        setPlayback({ kind: "idle" });
        toast.error(await readPreviewFailureMessage(resp));
        return;
      }

      const blob = await resp.blob();
      const url = URL.createObjectURL(blob);
      audioUrlRef.current = url;
      setPlayback({ kind: "ready", url });
    } catch {
      setPlayback({ kind: "idle" });
      toast.error("Network error — check your connection");
    }
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
              disabled={playback.kind === "loading"}
            >
              {playback.kind === "loading" ? "Rendering…" : "Play"}
            </Button>
            {playback.kind === "ready" && (
              <audio controls src={playback.url} aria-label="Persona preview audio" />
            )}
          </div>
        </div>
      )}
    </div>
  );
}
