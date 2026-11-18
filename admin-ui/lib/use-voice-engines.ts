"use client";

import { useEffect, useState } from "react";

/** One registry engine as `GET /api/voices/engines` lists it. */
export interface VoiceEngineEntry {
  name: string;
  profile: string;
  /** The render time limit in force for this engine, in seconds (a saved override, else its own configured value). */
  renderBudgetSeconds: number;
  /** The engine's own configured limit, in seconds: what it keeps when its override is cleared. */
  configRenderBudgetSeconds: number;
}

export interface VoiceEngines {
  /** The engine speech is rendered with right now. */
  active: string;
  engines: VoiceEngineEntry[];
}

export type VoiceEnginesState =
  | { kind: "loading" }
  | { kind: "loaded"; data: VoiceEngines }
  | { kind: "error" };

function isVoiceEngines(raw: unknown): raw is VoiceEngines {
  if (typeof raw !== "object" || raw === null) return false;
  const obj = raw as Record<string, unknown>;
  return (
    typeof obj["active"] === "string" &&
    Array.isArray(obj["engines"]) &&
    obj["engines"].every((entry: unknown) => {
      if (typeof entry !== "object" || entry === null) return false;
      const e = entry as Record<string, unknown>;
      return (
        typeof e["name"] === "string" &&
        typeof e["profile"] === "string" &&
        typeof e["renderBudgetSeconds"] === "number" &&
        typeof e["configRenderBudgetSeconds"] === "number"
      );
    })
  );
}

/**
 * The one `GET /api/voices/engines` fetch+parse (SPEC F219.2) — the configured voice engines and
 * which one is active. Same shape and tradeoffs as `usePersonaList`: one fetch per mount, no
 * cross-component cache; a changed `refreshKey` (e.g. the just-saved value) refetches, keeping the
 * previous list on screen meanwhile; a failed or malformed response is `error`, never a throw.
 */
export function useVoiceEngines(refreshKey?: string): VoiceEnginesState {
  const [state, setState] = useState<VoiceEnginesState>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;

    async function load(): Promise<void> {
      try {
        const resp = await fetch("/api/voices/engines", { credentials: "include" });
        const raw: unknown = resp.ok ? await resp.json() : null;
        if (cancelled) return;
        setState(isVoiceEngines(raw) ? { kind: "loaded", data: raw } : { kind: "error" });
      } catch {
        if (!cancelled) setState({ kind: "error" });
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, [refreshKey]);

  return state;
}
