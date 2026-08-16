"use client";

import { useEffect, useState } from "react";

/** One item on an installed avatar pack (SPEC F128.1/.5, PLAN T294) — the two fields the
 * apply-from-pack picker and the bulk-suggestion toolbar actually need. Declared locally rather
 * than importing `wardrobe/types.ts`'s own `AvatarPackSummaryItemDto` — the same
 * `usePersonaDirectory` discipline (this shared hook shouldn't couple to a type another route
 * segment owns, since the two happen to be wire-identical only by coincidence today). */
export interface AvatarPackEntry {
  name: string;
  suggestedPersona: string | null;
}

/** One row of `GET /api/avatar-packs` (PLAN T294) — an installed avatar pack, metadata only, this
 * hook's own consumers' minimal shape. `name` is nullable — see `AvatarPackSummaryDto`'s own
 * remarks (Host) for the should-never-happen re-parse-failure case this degrades. */
export interface AvatarPackListEntry {
  slug: string;
  name: string | null;
  items: AvatarPackEntry[];
}

export type AvatarPacksState =
  | { kind: "loading" }
  | { kind: "loaded"; packs: AvatarPackListEntry[] }
  | { kind: "error" };

function isAvatarPackEntry(raw: unknown): raw is AvatarPackEntry {
  if (typeof raw !== "object" || raw === null) return false;
  const obj = raw as Record<string, unknown>;
  return (
    typeof obj["name"] === "string" &&
    (obj["suggestedPersona"] === null || typeof obj["suggestedPersona"] === "string")
  );
}

function isAvatarPackListEntry(raw: unknown): raw is AvatarPackListEntry {
  if (typeof raw !== "object" || raw === null) return false;
  const obj = raw as Record<string, unknown>;
  return (
    typeof obj["slug"] === "string" &&
    (obj["name"] === null || typeof obj["name"] === "string") &&
    Array.isArray(obj["items"]) &&
    obj["items"].every(isAvatarPackEntry)
  );
}

function isAvatarPackListEntryList(raw: unknown): raw is AvatarPackListEntry[] {
  return Array.isArray(raw) && raw.every(isAvatarPackListEntry);
}

/**
 * The one `GET /api/avatar-packs` fetch+parse implementation (mirrors `useVoiceList`'s own "one
 * house read, several consumers" reasoning) — every installed avatar pack, items included. Feeds
 * both the persona editor's apply-from-pack picker and the roster toolbar's bulk-suggestion
 * confirm (PLAN T296): each mounts its own independent fetch, no cross-component cache, the same
 * tradeoff `useVoiceList` already accepts. One fetch per mount, no polling — an operator installing
 * a new pack mid-edit is rare enough that a page refresh (or re-opening the editor) is an
 * acceptable way to see it, not a gap worth polling for.
 */
export function useAvatarPacks(): AvatarPacksState {
  const [state, setState] = useState<AvatarPacksState>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;

    async function load(): Promise<void> {
      try {
        const resp = await fetch("/api/avatar-packs");
        if (!resp.ok) {
          if (!cancelled) setState({ kind: "error" });
          return;
        }
        const raw = (await resp.json()) as unknown;
        if (!isAvatarPackListEntryList(raw)) {
          if (!cancelled) setState({ kind: "error" });
          return;
        }
        if (!cancelled) setState({ kind: "loaded", packs: raw });
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
