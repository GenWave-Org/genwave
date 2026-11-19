"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { toast } from "@/components/ui/toast";

/** What one `POST /api/tts/preview` came to: the rendered audio, or a message fit for a toast. */
export type TtsPreviewOutcome = { kind: "ok"; blob: Blob } | { kind: "error"; message: string };

/**
 * The one `POST /api/tts/preview` call (a synchronous wav render). `readFailure` turns a non-2xx response into the toast
 * text, because each caller reads the endpoint's problem body its own way. Never throws: a network failure is an
 * `error` outcome.
 */
export async function requestTtsPreview(
  body: unknown,
  readFailure: (resp: Response) => Promise<string>
): Promise<TtsPreviewOutcome> {
  try {
    const resp = await fetch("/api/tts/preview", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    if (!resp.ok) return { kind: "error", message: await readFailure(resp) };
    return { kind: "ok", blob: await resp.blob() };
  } catch {
    return { kind: "error", message: "Network error — check your connection" };
  }
}

export interface TtsPreviewPlayer {
  /** A blob URL for an `<audio>` element once a render has finished, else null. */
  url: string | null;
  isRendering: boolean;
  /** Renders with `request`, toasting a failure; the previous audio is dropped first. */
  hear: (request: () => Promise<TtsPreviewOutcome>) => Promise<void>;
  /** Drops the audio, e.g. because what it was rendered from changed. */
  clear: () => void;
}

/**
 * The "Hear it" lifecycle shared by every audition button: render, hold the audio as a blob URL, and revoke that URL
 * when it is replaced or the component goes away, so a closed panel never leaks a blob for the life of the page.
 */
export function useTtsPreviewPlayer(): TtsPreviewPlayer {
  const [url, setUrl] = useState<string | null>(null);
  const [isRendering, setIsRendering] = useState(false);
  const urlRef = useRef<string | null>(null);

  useEffect(
    () => () => {
      if (urlRef.current !== null) URL.revokeObjectURL(urlRef.current);
    },
    []
  );

  const clear = useCallback((): void => {
    if (urlRef.current !== null) URL.revokeObjectURL(urlRef.current);
    urlRef.current = null;
    setUrl(null);
  }, []);

  const hear = useCallback(
    async (request: () => Promise<TtsPreviewOutcome>): Promise<void> => {
      setIsRendering(true);
      clear();
      try {
        const outcome = await request();
        if (outcome.kind === "error") {
          toast.error(outcome.message);
          return;
        }
        const next = URL.createObjectURL(outcome.blob);
        urlRef.current = next;
        setUrl(next);
      } finally {
        setIsRendering(false);
      }
    },
    [clear]
  );

  return { url, isRendering, hear, clear };
}
