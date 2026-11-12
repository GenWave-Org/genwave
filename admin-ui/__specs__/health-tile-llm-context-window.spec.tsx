// @jest-environment jsdom
// STORY-539 — the LLM tile is red under the 8k context floor (SPEC F218.21, the admin-ui half · PLAN T699)

import { describe, it, expect, afterEach } from "@jest/globals";
import { render, screen, cleanup } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { StatusTiles } from "../app/(authed)/dashboard/StatusTiles";
import type { StatusResponse } from "@/lib/broadcast-api";

function makeStatus(memoryOffReason: string | null): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 5 },
    llm: {
      enabled: true,
      model: "mistral-nemo:12b",
      activePersona: null,
      lastOutcome: "ok",
      lastAttemptAt: null,
      memoryOffReason,
    },
    voice: { engine: "kokoro", degraded: false, reason: null, checkedAt: null },
  };
}

afterEach(cleanup);

describe("the LLM tile under the 8k floor", () => {
  it("names why memory is off", () => {
    render(<StatusTiles status={makeStatus("context window 4096 < 8192, memory off")} error={false} timeZone="UTC" />);
    expect(screen.getByText("context window 4096 < 8192, memory off")).toBeInTheDocument();
  });

  it("shows no memory line when memory is on", () => {
    render(<StatusTiles status={makeStatus(null)} error={false} timeZone="UTC" />);
    expect(screen.queryByText(/memory off/)).not.toBeInTheDocument();
  });
});
