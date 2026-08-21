// Client-side wire shape + fetcher for the LLM call inspector (PLAN T41/T334, STORY-196/353, SPEC
// F73.1-F73.2, F139.2). Browser fetches go through the Next.js same-origin rewrite
// (/api/* -> api:8080), same convention as lib/booth-log-api.ts — never lib/api.ts's apiGet, which
// is server-only.

/**
 * One completed LLM call (SPEC F73.1) — `status`/`mode` are plain strings on the wire
 * (GenWave.Host.Api.LlmCallDto), not closed unions: a value this admin UI doesn't specifically
 * style still renders as its raw text rather than vanishing, the same "never drop an unknown kind"
 * discipline lib/booth-log-api.ts's own BoothLogEntry already documents.
 */
export interface LlmCallEntry {
  seq: number;
  /** gh-#429 — who authored this call's copy, or `null` for a persona-less render (never `""`). */
  personaName: string | null;
  startedAt: string;
  elapsedMs: number;
  status: string;
  statusDetail: string | null;
  mode: string;
  promptSystem: string | null;
  promptUser: string | null;
  response: string | null;
  promptChars: number;
  responseChars: number;
  /** gh-#385, SPEC F127.11 — which generation surface produced this call
   * (GenWave.Tts.LlmCallKind): `"copy"` for every ordinary segment-copy call, `"crosstalk"` for a
   * CrosstalkScriptWriter call — so an operator can tell "why was there no banter" apart from an
   * ordinary blurb miss. Plain string, not a closed union, for the same reason `status`/`mode`
   * are above. */
  kind: string;
  /** SPEC F139.1, STORY-353, PLAN T334 — WHY this call resolved the way it did
   * (GenWave.Tts.LlmCallCause): a finer-grained sibling of `status` above (e.g. a `"failed"`
   * status might be a `"timeout"` or a `"connectionfailure"` cause). Plain string, not a closed
   * union, same reason as `status`/`mode`/`kind`. */
  cause: string;
  /** SPEC F139.2, STORY-353, PLAN T334 — the completions model this call used. Never `null`, same
   * as the wire's own `GenWave.Host.Api.LlmCallDto.Model`. */
  model: string;
}

/**
 * GET /api/llm-calls's `causeSummary` (SPEC F139.2, STORY-353, PLAN T334) — one row of the
 * rolling 24h by-(cause, model, kind) count, riding the SAME response as {@link LlmCallEntry}
 * (see `GenWave.Host.Api.LlmCallsResponseDto`'s own remarks for why one response, not two). Not
 * yet consumed by this page's own UI (the dashboard's health tile computes its own dominant-cause
 * line from a smaller, `/api/status`-scoped read instead — `broadcast-api.ts`'s own
 * `StatusResponse.llm`) — this type exists so the wire's own shape stays fully typed for whichever
 * future admin surface reads it.
 */
export interface LlmCallCauseSummaryEntry {
  cause: string;
  model: string;
  kind: string;
  count: number;
}

/** Wire shape of `GET /api/llm-calls` itself (SPEC F139.2, PLAN T334) — mirrors
 * `GenWave.Host.Api.LlmCallsResponseDto`. */
interface LlmCallsResponse {
  calls: LlmCallEntry[];
  causeSummary: LlmCallCauseSummaryEntry[];
}

/**
 * GET /api/llm-calls (SPEC F73.1-F73.2, F139.2) — every call the ring currently holds, newest
 * first. No paging: the ring is capped at a small size (~50) by construction, so the whole
 * response is always a single, small round-trip. Returns only {@link LlmCallEntry}'s own `calls`
 * array — this page's presentational components (`LlmCallsFeed`) never needed the `causeSummary`
 * half added alongside it at T334, so there is no reason to thread an unused field through
 * `useLlmCalls`/`LlmCallsView` just because the wire happens to carry it.
 */
export async function fetchLlmCalls(): Promise<LlmCallEntry[]> {
  const response = await fetch("/api/llm-calls", {
    credentials: "include",
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`GET /api/llm-calls failed: ${response.status}`);
  }
  const body = (await response.json()) as LlmCallsResponse;
  return body.calls;
}
