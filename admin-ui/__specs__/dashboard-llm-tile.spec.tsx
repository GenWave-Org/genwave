// @jest-environment jsdom
// STORY-125 — LLM health visible on the dashboard (UI half, SPEC F34.8)
//
// Runner: Jest (jsdom) + @testing-library/react. Drives DashboardView with a mocked global.fetch
// dispatched by URL substring across the three polled endpoints (now-playing, status,
// play-history) — mirrors safe-scope-depleted-badge.spec.tsx's dashboard-side fetch mock (itself
// pared from dashboard-page.spec.tsx's per-endpoint dispatch), since these specs only exercise the
// dashboard's LLM tile. The api half is Story125_LlmStatus.cs.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { DashboardView } from "../app/(authed)/dashboard/DashboardView";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const ISO_NOW = "2026-01-01T12:00:00.000Z";

interface LlmOverrides {
  enabled?: boolean;
  model?: string | null;
  activePersona?: string | null;
  lastOutcome?: "ok" | "failed" | null;
  lastAttemptAt?: string | null;
}

/** GET /api/status body — catalog/safeScope are fixed benign values; only `llm` varies per spec. */
function makeStatus(llm: LlmOverrides = {}): unknown {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 5 },
    llm: {
      enabled: false,
      model: null,
      activePersona: null,
      lastOutcome: null,
      lastAttemptAt: null,
      ...llm,
    },
  };
}

interface FetchState {
  status: unknown;
}

function endpointKeyFor(url: string): "now" | "status" | "history" {
  if (url.includes("now-playing")) return "now";
  if (url.includes("play-history")) return "history";
  return "status";
}

/** now-playing/history are fixed to benign values — these specs exercise the LLM tile only. */
function installFetchMock(initialStatus: unknown): {
  fn: jest.MockedFunction<typeof fetch>;
  state: FetchState;
} {
  const state: FetchState = { status: initialStatus };
  const fn = jest.fn<typeof fetch>().mockImplementation((input) => {
    const key = endpointKeyFor(String(input));
    if (key === "now") {
      return Promise.resolve({ ok: false, status: 503, json: () => Promise.resolve({}) } as Response);
    }
    if (key === "history") {
      return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve([]) } as Response);
    }
    return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(state.status) } as Response);
  });
  global.fetch = fn as unknown as typeof fetch;
  return { fn, state };
}

/** Flushes the initial-mount poll without advancing fake time. */
async function flush(): Promise<void> {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(0);
  });
}

/** Advances fake time and flushes the resulting fetch/json promise chain(s). */
async function advance(ms: number): Promise<void> {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(ms);
  });
}

beforeEach(() => {
  jest.useFakeTimers({ now: new Date(ISO_NOW) });
});

afterEach(() => {
  jest.useRealTimers();
  jest.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// Feature: LLM health visible on the dashboard
// ---------------------------------------------------------------------------

describe("Feature: LLM health visible on the dashboard", () => {
  describe("Scenario: tile states from /api/status", () => {
    it("renders neutral when llm.enabled is false", async () => {
      installFetchMock(makeStatus({ enabled: false }));

      render(<DashboardView timeZone="UTC" />);
      await flush();

      const tile = screen.getByRole("group", { name: "LLM" });
      expect(tile.className).toMatch(/\bborder-line\b/);
      expect(tile.className).not.toMatch(/\bborder-success\b/);
      expect(tile.className).not.toMatch(/\bborder-danger\b/);
      expect(screen.getByText("Off")).toBeInTheDocument();
    });

    it("renders ok when the last attempt succeeded", async () => {
      installFetchMock(makeStatus({ enabled: true, model: "gpt-4o-mini", lastOutcome: "ok" }));

      render(<DashboardView timeZone="UTC" />);
      await flush();

      const tile = screen.getByRole("group", { name: "LLM" });
      expect(tile.className).toMatch(/\bborder-success\b/);
      expect(screen.getByText("gpt-4o-mini")).toBeInTheDocument();
    });

    it("renders a warning (F31.5 token styling) when enabled and the last attempt failed", async () => {
      installFetchMock(makeStatus({ enabled: true, model: "gpt-4o-mini", lastOutcome: "failed" }));

      render(<DashboardView timeZone="UTC" />);
      await flush();

      const tile = screen.getByRole("group", { name: "LLM" });
      expect(tile.className).toMatch(/\bborder-danger\b/);
      expect(screen.getByText(/Last completion failed/)).toBeInTheDocument();
    });

    it("shows the active persona name when one is set", async () => {
      installFetchMock(
        makeStatus({ enabled: true, model: "gpt-4o-mini", lastOutcome: "ok", activePersona: "Neon Nightowl" })
      );

      render(<DashboardView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("Neon Nightowl")).toBeInTheDocument();
    });

    it("reflects a state change within one poll interval", async () => {
      const { state } = installFetchMock(makeStatus({ enabled: true, model: "gpt-4o-mini", lastOutcome: "ok" }));

      render(<DashboardView timeZone="UTC" />);
      await flush();
      expect(screen.getByRole("group", { name: "LLM" }).className).toMatch(/\bborder-success\b/);

      state.status = makeStatus({ enabled: true, model: "gpt-4o-mini", lastOutcome: "failed" });
      await advance(5000);

      expect(screen.getByRole("group", { name: "LLM" }).className).toMatch(/\bborder-danger\b/);
    });
  });
});
