"use client";

import type { ReactNode } from "react";
import { LlmCallsFeed } from "./LlmCallsFeed";
import { useLlmCalls } from "./useLlmCalls";

interface LlmCallsViewProps {
  /** Test-only injection point, threaded to the row timestamp formatter; production omits this and gets the browser's local zone. */
  timeZone?: string;
}

/**
 * The LLM calls tab's content (PLAN T41, STORY-196, SPEC F73.1-F73.2): the admin call inspector —
 * prompt/response/timing/status/mode for the last ~50 LLM calls, newest first. State lives in
 * `useLlmCalls`; this component only wires it to the presentational `LlmCallsFeed`.
 */
export function LlmCallsView({ timeZone }: LlmCallsViewProps = {}): ReactNode {
  const { entries, error } = useLlmCalls();

  return <LlmCallsFeed entries={entries} error={error} timeZone={timeZone} />;
}
