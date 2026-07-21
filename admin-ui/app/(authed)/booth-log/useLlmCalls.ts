"use client";

import { usePoll } from "@/lib/use-poll";
import { fetchLlmCalls, type LlmCallEntry } from "@/lib/llm-calls-api";

/** Within PLAN T41's 10-15s guidance — same poll family as the booth log's own 12s cadence
 * (usePoll, unchanged pause/resume/degrade contract): the inspector is a debug glance, not a
 * now-playing readout, so it doesn't need the Dashboard/Live pages' faster 5s tick. */
const LLM_CALLS_POLL_INTERVAL_MS = 12000;

export interface UseLlmCallsResult {
  /** Newest-first, exactly as the endpoint returns it. `null` until the first poll resolves. */
  entries: LlmCallEntry[] | null;
  /** True when the most recent poll failed; `entries` is left untouched (usePoll's contract — a
   * caller renders a quiet degrade, never discards what's already loaded). */
  error: boolean;
}

/**
 * LLM call inspector poll state (PLAN T41, STORY-196, SPEC F73.1-F73.2) — a thin wrapper over the
 * shared `usePoll` hook; unlike the booth log's own `useBoothLogFeed`, there is no paging to
 * accumulate here (the ring's whole contents are one small response), so this only needs to expose
 * the poll's own `data`/`error`.
 */
export function useLlmCalls(): UseLlmCallsResult {
  const poll = usePoll(() => fetchLlmCalls(), { intervalMs: LLM_CALLS_POLL_INTERVAL_MS });
  return { entries: poll.data, error: poll.error };
}
