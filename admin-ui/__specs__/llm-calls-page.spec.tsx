// @jest-environment jsdom
// PLAN T41 — Admin UI: LLM call inspector tab (STORY-196, SPEC F73.1-F73.2)
//
// Runner: Jest (jsdom) + @testing-library/react. Drives LlmCallsView (the client component the
// Booth log page's `?tab=llm-calls` renders) with a mocked global.fetch — mirrors
// booth-log-page.spec.tsx's own installFetchMock style, simplified since this endpoint has no
// paging (one flat array, newest-first, capped at ring size).
//
// The tab-strip scenario at the bottom covers BoothLogTabs directly (URL-driven active state, no
// client state) — mirrors responsive-a11y.spec.tsx's own CatalogTabs coverage.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, act, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";
import { LlmCallsView } from "../app/(authed)/booth-log/LlmCallsView";
import { BoothLogTabs } from "../app/(authed)/booth-log/BoothLogTabs";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const ISO_NOW = "2026-07-20T12:00:00.000Z";

interface EntryOverrides {
  seq?: number;
  startedAt?: string;
  elapsedMs?: number;
  status?: string;
  statusDetail?: string | null;
  mode?: string;
  promptSystem?: string | null;
  promptUser?: string | null;
  response?: string | null;
  promptChars?: number;
  responseChars?: number;
}

function makeEntry(overrides: EntryOverrides = {}) {
  return {
    seq: 1,
    startedAt: "2026-07-20T11:58:00.000Z",
    elapsedMs: 340,
    status: "ok",
    statusDetail: null,
    mode: "normal",
    promptSystem: "You are a warm, upbeat radio DJ. Style: moody, late-night.",
    promptUser: "Segment: LeadIn. Track: Astral Plane by Valerie June.",
    response: "Coming up, a deep cut to ease into the evening.",
    promptChars: 100,
    responseChars: 48,
    ...overrides,
  };
}

type MockResult = { kind: "ok"; body: unknown } | { kind: "network-error" };

function ok(body: unknown): MockResult {
  return { kind: "ok", body };
}

function networkError(): MockResult {
  return { kind: "network-error" };
}

function installFetchMock(initial: MockResult) {
  const state = { current: initial };
  const fn = jest.fn<typeof fetch>().mockImplementation(() => {
    const result = state.current;
    if (result.kind === "network-error") {
      return Promise.reject(new Error("network error"));
    }
    return Promise.resolve({
      ok: true,
      status: 200,
      json: () => Promise.resolve(result.body),
    } as Response);
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

/** Advances fake time and flushes the resulting fetch/json promise chain. */
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
// Feature: LLM call inspector
// ---------------------------------------------------------------------------

describe("Feature: LLM call inspector", () => {
  describe("Scenario: call rows render time, status/mode chips, elapsed, and a response preview", () => {
    it("renders one row per call, newest first, with the wire's own order preserved", async () => {
      const entries = [
        makeEntry({ seq: 3, startedAt: "2026-07-20T11:59:00.000Z", elapsedMs: 210, status: "ok", mode: "normal" }),
        makeEntry({ seq: 2, startedAt: "2026-07-20T11:58:30.000Z", elapsedMs: 340, status: "failed", statusDetail: "HTTP 500", mode: "soft", response: null }),
        makeEntry({ seq: 1, startedAt: "2026-07-20T11:58:00.000Z", elapsedMs: 5002, status: "timeout", statusDetail: "Llm:TimeoutSeconds exceeded", mode: "hard", response: null }),
      ];
      installFetchMock(ok(entries));

      render(<LlmCallsView timeZone="UTC" />);
      await flush();

      const rows = screen.getAllByRole("row");
      expect(rows).toHaveLength(entries.length + 1); // +1 header row

      expect(screen.getByText("Ok")).toBeInTheDocument();
      expect(screen.getByText("Failed")).toBeInTheDocument();
      expect(screen.getByText("Timeout")).toBeInTheDocument();
      expect(screen.getByText("Normal")).toBeInTheDocument();
      expect(screen.getByText("Soft")).toBeInTheDocument();
      expect(screen.getByText("Hard")).toBeInTheDocument();
      expect(screen.getByText("340 ms")).toBeInTheDocument();

      // Wire order is preserved exactly (newest-first, SPEC F73.1) — no client-side re-sort.
      const rowTexts = rows.slice(1).map((row) => row.textContent ?? "");
      expect(rowTexts[0]).toContain("Ok");
      expect(rowTexts[rowTexts.length - 1]).toContain("Timeout");
    });

    it("falls back to the status detail as the response-column preview when there is no response", async () => {
      installFetchMock(ok([makeEntry({ status: "failed", statusDetail: "HTTP 500", response: null })]));

      render(<LlmCallsView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("HTTP 500")).toBeInTheDocument();
    });
  });

  describe("Scenario: expandable rows show the full prompt/response text", () => {
    it("hides the full prompt/response until Details is clicked, then reveals it", async () => {
      installFetchMock(ok([makeEntry()]));

      render(<LlmCallsView timeZone="UTC" />);
      await flush();

      // Before expanding, only the response-column preview is present — never the prompts, and
      // the response itself appears exactly once (the preview cell).
      expect(screen.queryByText("You are a warm, upbeat radio DJ. Style: moody, late-night.")).not.toBeInTheDocument();
      expect(screen.queryByText("Segment: LeadIn. Track: Astral Plane by Valerie June.")).not.toBeInTheDocument();
      expect(screen.getAllByText("Coming up, a deep cut to ease into the evening.")).toHaveLength(1);

      fireEvent.click(screen.getByRole("button", { name: "Details" }));

      // After expanding, the prompts appear (previously absent) and the response now appears
      // twice — the still-visible preview cell plus the new detail panel.
      expect(screen.getByText("You are a warm, upbeat radio DJ. Style: moody, late-night.")).toBeInTheDocument();
      expect(screen.getByText("Segment: LeadIn. Track: Astral Plane by Valerie June.")).toBeInTheDocument();
      expect(screen.getAllByText("Coming up, a deep cut to ease into the evening.")).toHaveLength(2);
    });

    it("collapses again when Hide is clicked", async () => {
      installFetchMock(ok([makeEntry()]));

      render(<LlmCallsView timeZone="UTC" />);
      await flush();

      fireEvent.click(screen.getByRole("button", { name: "Details" }));
      expect(screen.getByText("Segment: LeadIn. Track: Astral Plane by Valerie June.")).toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: "Hide" }));
      expect(screen.queryByText("Segment: LeadIn. Track: Astral Plane by Valerie June.")).not.toBeInTheDocument();
      // The preview cell's own copy of the response text is unaffected by the collapse.
      expect(screen.getAllByText("Coming up, a deep cut to ease into the evening.")).toHaveLength(1);
    });

    it("shows the status detail in the expanded panel for a failed call", async () => {
      installFetchMock(ok([makeEntry({ status: "failed", statusDetail: "HTTP 500", response: null })]));

      render(<LlmCallsView timeZone="UTC" />);
      await flush();

      fireEvent.click(screen.getByRole("button", { name: "Details" }));

      expect(screen.getByText("Status detail")).toBeInTheDocument();
      expect(screen.getAllByText("HTTP 500").length).toBeGreaterThan(0);
    });
  });

  describe("Scenario: shares the booth log's poll cadence family", () => {
    it("polls again after 12s and pauses while the tab is hidden", async () => {
      Object.defineProperty(document, "hidden", { value: false, configurable: true });
      const { fn } = installFetchMock(ok([]));

      render(<LlmCallsView timeZone="UTC" />);
      await flush();
      const initialCalls = fn.mock.calls.length;

      await advance(12000);
      expect(fn.mock.calls.length).toBeGreaterThan(initialCalls);

      Object.defineProperty(document, "hidden", { value: true, configurable: true });
      await act(async () => {
        document.dispatchEvent(new Event("visibilitychange"));
      });
      const countAfterHide = fn.mock.calls.length;

      await advance(15000);
      expect(fn.mock.calls.length).toBe(countAfterHide);

      Object.defineProperty(document, "hidden", { value: false, configurable: true });
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: empty ring and a poll failure with designed states (sad path)", () => {
    it("renders the EmptyState explaining calls appear as the LLM is reached", async () => {
      installFetchMock(ok([]));

      render(<LlmCallsView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("No LLM calls yet")).toBeInTheDocument();
    });

    it("shows an unavailable message before the first successful poll", async () => {
      installFetchMock(networkError());

      render(<LlmCallsView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("LLM calls — unavailable")).toBeInTheDocument();
    });

    it("degrades quietly on a later poll failure without discarding loaded entries", async () => {
      const { state } = installFetchMock(ok([makeEntry()]));

      render(<LlmCallsView timeZone="UTC" />);
      await flush();
      expect(screen.getByText("Coming up, a deep cut to ease into the evening.")).toBeInTheDocument();

      state.current = networkError();
      await advance(12000);

      expect(screen.getByText("LLM calls unavailable — retrying…")).toBeInTheDocument();
      expect(screen.getByText("Coming up, a deep cut to ease into the evening.")).toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: Booth log / LLM calls tab strip
// ---------------------------------------------------------------------------

describe("Feature: Booth log tab strip", () => {
  it("marks Booth log active and links LLM calls to ?tab=llm-calls when on the log tab", () => {
    render(<BoothLogTabs activeTab="log" />);

    const logLink = screen.getByRole("link", { name: "Booth log" });
    const llmLink = screen.getByRole("link", { name: "LLM calls" });

    expect(logLink).toHaveAttribute("aria-current", "page");
    expect(llmLink).not.toHaveAttribute("aria-current");
    expect(llmLink).toHaveAttribute("href", "/booth-log?tab=llm-calls");
  });

  it("marks LLM calls active when on that tab", () => {
    render(<BoothLogTabs activeTab="llm-calls" />);

    expect(screen.getByRole("link", { name: "LLM calls" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Booth log" })).not.toHaveAttribute("aria-current");
  });
});
