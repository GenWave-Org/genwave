"use client";

import { useEffect, useState } from "react";

/** One row of the persona roster this hook feeds — just the two fields a picker control needs. */
export interface PersonaListEntry {
  id: number;
  name: string;
}

export type PersonaListState =
  | { kind: "loading" }
  | { kind: "loaded"; personas: PersonaListEntry[] }
  | { kind: "error" };

function isPersonaListEntryList(raw: unknown): raw is PersonaListEntry[] {
  return (
    Array.isArray(raw) &&
    raw.every((entry) => {
      if (typeof entry !== "object" || entry === null) return false;
      const obj = entry as Record<string, unknown>;
      return typeof obj["id"] === "number" && typeof obj["name"] === "string";
    })
  );
}

/**
 * The one `GET /api/personas` fetch+parse implementation for a control that needs the full
 * id/name roster to build a picker (SPEC F79.5's "never a second listing path" idiom, applied to
 * personas the same way `useVoiceList` applies it to voices — `PersonaSettingControl` is this
 * hook's only caller today, gh-#426).
 *
 * A sibling of `usePersonaDirectory` (`lib/use-persona-directory.ts`), not a reuse: that hook
 * resolves a bare `personaId` to a display name for the booth log / now-playing surfaces and
 * returns a `Map`; this one feeds a `<select>` of every persona in server order and returns a
 * plain array. Each call mounts an independent fetch — no cross-component cache — the same
 * tradeoff `useVoiceList` already documents and accepts for its own callers.
 */
export function usePersonaList(): PersonaListState {
  const [status, setStatus] = useState<PersonaListState>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;

    async function loadPersonas(): Promise<void> {
      try {
        const resp = await fetch("/api/personas", { credentials: "include" });
        if (!resp.ok) {
          if (!cancelled) setStatus({ kind: "error" });
          return;
        }
        const raw = (await resp.json()) as unknown;
        if (!isPersonaListEntryList(raw)) {
          if (!cancelled) setStatus({ kind: "error" });
          return;
        }
        if (!cancelled) setStatus({ kind: "loaded", personas: raw });
      } catch {
        if (!cancelled) setStatus({ kind: "error" });
      }
    }

    void loadPersonas();
    return () => {
      cancelled = true;
    };
  }, []);

  return status;
}
