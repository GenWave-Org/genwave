"use client";

import { useEffect, useState } from "react";

export type VoiceListState =
  | { kind: "loading" }
  | { kind: "loaded"; voices: string[] }
  | { kind: "error" };

function isVoiceIdList(raw: unknown): raw is string[] {
  return Array.isArray(raw) && raw.every((entry) => typeof entry === "string");
}

/**
 * The one `GET /api/voices` fetch+parse implementation (SPEC F79.5 — "never a second
 * voice-listing path"). `VoiceControl` calls this to feed its own dropdown; the persona
 * import-warning derivation (`usePersonaVoiceWarning`) calls it again to learn the engine's live
 * list for comparison. Each call mounts an independent fetch — no cross-component cache — so two
 * mounts on the same page cost one extra GET, a deliberate tradeoff rather than coupling the
 * dropdown's render lifecycle to the warning check's. `Station:Voice`'s own settings-page control
 * no longer calls this (STORY-479 retired it in favor of the live `choices` list `GET
 * /api/settings` itself now carries) — `VoiceControl` and `usePersonaVoiceWarning` remain.
 */
export function useVoiceList(): VoiceListState {
  const [status, setStatus] = useState<VoiceListState>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;

    async function loadVoices(): Promise<void> {
      try {
        const resp = await fetch("/api/voices");
        if (!resp.ok) {
          if (!cancelled) setStatus({ kind: "error" });
          return;
        }
        const raw = (await resp.json()) as unknown;
        if (!isVoiceIdList(raw)) {
          if (!cancelled) setStatus({ kind: "error" });
          return;
        }
        if (!cancelled) setStatus({ kind: "loaded", voices: raw });
      } catch {
        if (!cancelled) setStatus({ kind: "error" });
      }
    }

    void loadVoices();
    return () => {
      cancelled = true;
    };
  }, []);

  return status;
}
