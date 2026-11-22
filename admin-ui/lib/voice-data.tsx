"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { isVoiceEngines, type VoiceEngines, type VoiceEnginesState } from "./use-voice-engines";
import type { VoiceListState } from "./use-voice-list";

function isVoiceIdList(raw: unknown): raw is string[] {
  return Array.isArray(raw) && raw.every((entry) => typeof entry === "string");
}

async function getJson(url: string): Promise<unknown> {
  try {
    const resp = await fetch(url, { credentials: "include" });
    return resp.ok ? ((await resp.json()) as unknown) : null;
  } catch {
    return null;
  }
}

/**
 * The voice engines and each engine's voice list, each asked for once (SPEC F219.10). A page with several voice
 * pickers shares one store through {@link VoiceDataProvider}, so a page of N pickers makes one engines request and one
 * list request per engine, not N of each. The server does not cache non-Kokoro lists, which is why this does.
 * A failed or malformed response is remembered as such for the life of the store (a reload tries again).
 */
export class VoiceDataStore {
  private enginesRequest: Promise<VoiceEngines | null> | null = null;
  private readonly listRequests = new Map<string, Promise<string[] | null>>();

  engines(): Promise<VoiceEngines | null> {
    this.enginesRequest ??= getJson("/api/voices/engines").then((raw) => (isVoiceEngines(raw) ? raw : null));
    return this.enginesRequest;
  }

  /** An engine with no voice list (Piper) is a loaded empty list. */
  list(engine: string): Promise<string[] | null> {
    let request = this.listRequests.get(engine);
    if (request === undefined) {
      request = getJson(`/api/voices?engine=${encodeURIComponent(engine)}`).then((raw) =>
        isVoiceIdList(raw) ? raw : null
      );
      this.listRequests.set(engine, request);
    }
    return request;
  }
}

const VoiceDataContext = createContext<VoiceDataStore | null>(null);

/** Shares one {@link VoiceDataStore} with every voice picker below it. */
export function VoiceDataProvider({ children }: { children: ReactNode }): ReactNode {
  const [store] = useState(() => new VoiceDataStore());
  return <VoiceDataContext.Provider value={store}>{children}</VoiceDataContext.Provider>;
}

/** The surrounding provider's store, or one of its own when there is none (a lone picker). */
function useVoiceDataStore(): VoiceDataStore {
  const shared = useContext(VoiceDataContext);
  const [own] = useState(() => new VoiceDataStore());
  return shared ?? own;
}

/** `GET /api/voices/engines`, shared across the page (SPEC F219.2). */
export function useSharedVoiceEngines(): VoiceEnginesState {
  const store = useVoiceDataStore();
  const [state, setState] = useState<VoiceEnginesState>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;
    void store.engines().then((data) => {
      if (!cancelled) setState(data === null ? { kind: "error" } : { kind: "loaded", data });
    });
    return () => {
      cancelled = true;
    };
  }, [store]);

  return state;
}

/** One engine's voice list, `GET /api/voices?engine=<name>`, shared across the page (SPEC F219.10). */
export function useEngineVoiceList(engine: string): VoiceListState {
  const store = useVoiceDataStore();
  const [state, setState] = useState<VoiceListState>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;
    void store.list(engine).then((voices) => {
      if (!cancelled) setState(voices === null ? { kind: "error" } : { kind: "loaded", voices });
    });
    return () => {
      cancelled = true;
    };
  }, [store, engine]);

  return state;
}
