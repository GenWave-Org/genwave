"use client";

import { useEffect, useState } from "react";

/** The two fields of `PersonaDto` (app/(authed)/personas/types.ts) this directory actually needs
 * — declared locally rather than importing that route segment's full DTO, so this shared hook
 * doesn't couple to a type another feature owns (Y3's "feed components the wire's shape" lesson
 * applies to cross-feature reuse too, not just wire fidelity). */
interface PersonaDirectoryEntry {
  id: number;
  name: string;
}

export type PersonaDirectoryState =
  | { kind: "loading" }
  | { kind: "loaded"; names: ReadonlyMap<number, string> }
  | { kind: "error" };

function isPersonaDirectoryEntryList(raw: unknown): raw is PersonaDirectoryEntry[] {
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
 * Resolves persona id -> name for surfaces that only carry a bare `personaId` on the wire — the
 * booth log's stamped-attribution column (SPEC F84.6) and the now-playing taste thumb that shares
 * its resolution path. One fetch per mount, no polling/retry: the same call `PersonaSettingControl`
 * already makes for the same reason (a persona roster changes rarely enough that a poll would be
 * ceremony, not correctness).
 */
export function usePersonaDirectory(): PersonaDirectoryState {
  const [state, setState] = useState<PersonaDirectoryState>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;

    async function load(): Promise<void> {
      try {
        const resp = await fetch("/api/personas", { credentials: "include" });
        if (!resp.ok) {
          if (!cancelled) setState({ kind: "error" });
          return;
        }
        const raw = (await resp.json()) as unknown;
        if (!isPersonaDirectoryEntryList(raw)) {
          if (!cancelled) setState({ kind: "error" });
          return;
        }
        if (!cancelled) {
          setState({ kind: "loaded", names: new Map(raw.map((entry) => [entry.id, entry.name])) });
        }
      } catch {
        if (!cancelled) setState({ kind: "error" });
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}

/** Fallback copy for a personaId whose name hasn't resolved yet (directory still loading, or the
 * fetch failed) — the control this feeds is still rendered (SPEC F84.6 gates only on `personaId`
 * presence, never on whether the directory happens to have loaded), so this never blocks
 * rendering, it just degrades the label. */
export function personaNameOrFallback(state: PersonaDirectoryState, personaId: number): string {
  return state.kind === "loaded" ? (state.names.get(personaId) ?? "this persona") : "this persona";
}
