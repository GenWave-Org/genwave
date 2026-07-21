// Client-side wire shape + fetcher for the LLM call inspector (PLAN T41, STORY-196, SPEC
// F73.1-F73.2). Browser fetches go through the Next.js same-origin rewrite (/api/* -> api:8080),
// same convention as lib/booth-log-api.ts — never lib/api.ts's apiGet, which is server-only.

/**
 * One completed LLM call (SPEC F73.1) — `status`/`mode` are plain strings on the wire
 * (GenWave.Host.Api.LlmCallDto), not closed unions: a value this admin UI doesn't specifically
 * style still renders as its raw text rather than vanishing, the same "never drop an unknown kind"
 * discipline lib/booth-log-api.ts's own BoothLogEntry already documents.
 */
export interface LlmCallEntry {
  seq: number;
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
}

/**
 * GET /api/llm-calls (SPEC F73.1-F73.2) — every call the ring currently holds, newest first. No
 * paging: the ring is capped at a small size (~50) by construction, so the whole response is
 * always a single, small round-trip.
 */
export async function fetchLlmCalls(): Promise<LlmCallEntry[]> {
  const response = await fetch("/api/llm-calls", {
    credentials: "include",
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`GET /api/llm-calls failed: ${response.status}`);
  }
  return (await response.json()) as LlmCallEntry[];
}
