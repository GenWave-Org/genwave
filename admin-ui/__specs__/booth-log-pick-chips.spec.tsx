// @jest-environment jsdom
// STORY-217 — The booth log tells me why each track was picked (SPEC F86.3, F86.5 — UI half, T75)
//
// Runner: Jest + jsdom + @testing-library/react. Booth-log track rows whose API entry
// carries `pick` render one chip per fired rule (summary + signed weight, e.g.
// "artist: MacLeod +0.6") and an exploration badge when isExploration — chips XOR badge,
// never both (F86.5). Rows without pick data render byte-identically to today. The chip
// row is the shared PickChips component T76's Live card reuses.
//
// Driven against `BoothLogView`, mirroring persona-taste-thumbs.spec.tsx's
// installBoothLogFetchMock harness (itself extending booth-log-page.spec.tsx's own) — this
// exercises the real fetch -> BoothLogView -> BoothLogFeed -> PickChips wiring, not PickChips in
// isolation, so a broken wire-shape assumption (field name, absent-vs-null) fails here too.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { BoothLogView } from "../app/(authed)/booth-log/BoothLogView";
import type { BoothLogPick } from "../lib/booth-log-api";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const ISO_NOW = "2026-01-01T12:00:00.000Z";

interface BoothLogEntryFixture {
  occurredAt?: string;
  kind?: string;
  summary?: string;
  id?: number;
  personaId?: number | null;
  pick?: BoothLogPick;
}

function makeBoothLogEntry(overrides: BoothLogEntryFixture = {}) {
  return {
    occurredAt: "2026-01-01T10:04:00.000Z",
    kind: "track-started",
    summary: "Started 'Astral Plane' by Valerie June",
    id: 501,
    personaId: null,
    ...overrides,
  };
}

type MockResult = { kind: "ok"; status?: number; body: unknown } | { kind: "network-error" };

function ok(body: unknown, status = 200): MockResult {
  return { kind: "ok", status, body };
}

interface BoothLogFetchState {
  head: MockResult;
  personas: MockResult;
}

function endpointKeyForBoothLog(url: string): keyof BoothLogFetchState {
  if (url.includes("/api/personas")) return "personas";
  return "head";
}

function installBoothLogFetchMock(initial: BoothLogFetchState) {
  const state: BoothLogFetchState = { ...initial };
  const fn = jest.fn<typeof fetch>().mockImplementation((input) => {
    const url = String(input);
    const result = state[endpointKeyForBoothLog(url)];
    if (result.kind === "network-error") {
      return Promise.reject(new Error("network error"));
    }
    const status = result.status ?? 200;
    return Promise.resolve({
      ok: status >= 200 && status < 300,
      status,
      json: () => Promise.resolve(result.body),
    } as Response);
  });
  global.fetch = fn as unknown as typeof fetch;
  return { fn, state };
}

function renderBoothLog(): ReturnType<typeof render> {
  return render(<BoothLogView timeZone="UTC" />);
}

/** Flushes the initial-mount poll (or any already-scheduled microtasks) without advancing time. */
async function flush(): Promise<void> {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(0);
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
// Feature: booth-log why-this-pick chips
// ---------------------------------------------------------------------------

describe("Feature: booth-log why-this-pick chips", () => {
  describe("Scenario: a stamped rule-driven pick row", () => {
    // Arrange: one row stamped with two fired rules — a positive artist match and a negative
    // genre match — and isExploration false.
    const pick: BoothLogPick = {
      firedRules: [
        { summary: "The Weeknd", weight: 0.6 },
        { summary: "genre: Ambient", weight: -0.3 },
      ],
      isExploration: false,
    };

    it("renders one chip per fired rule", async () => {
      installBoothLogFetchMock({
        head: ok({ entries: [makeBoothLogEntry({ pick })], nextBefore: null }),
        personas: ok([]),
      });

      renderBoothLog();
      await flush();

      expect(screen.getByRole("list", { name: "Fired rules" }).children).toHaveLength(2);
    });

    it("shows each chip's summary with its signed weight", async () => {
      installBoothLogFetchMock({
        head: ok({ entries: [makeBoothLogEntry({ pick })], nextBefore: null }),
        personas: ok([]),
      });

      renderBoothLog();
      await flush();

      expect(screen.getByText("The Weeknd +0.6")).toBeInTheDocument();
      expect(screen.getByText("genre: Ambient -0.3")).toBeInTheDocument();
    });

    it("renders no exploration badge", async () => {
      installBoothLogFetchMock({
        head: ok({ entries: [makeBoothLogEntry({ pick })], nextBefore: null }),
        personas: ok([]),
      });

      renderBoothLog();
      await flush();

      expect(screen.queryByText("Exploration pick")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: a stamped exploration pick row", () => {
    // Arrange: one row stamped isExploration true — the ranker's own contract guarantees
    // firedRules is empty here (F83.2), but the component must not depend on that to stay XOR.
    const pick: BoothLogPick = { firedRules: [], isExploration: true };

    it("renders the exploration badge", async () => {
      installBoothLogFetchMock({
        head: ok({ entries: [makeBoothLogEntry({ pick })], nextBefore: null }),
        personas: ok([]),
      });

      renderBoothLog();
      await flush();

      expect(screen.getByText("Exploration pick")).toBeInTheDocument();
    });

    it("renders zero rule chips", async () => {
      installBoothLogFetchMock({
        head: ok({ entries: [makeBoothLogEntry({ pick })], nextBefore: null }),
        personas: ok([]),
      });

      renderBoothLog();
      await flush();

      expect(screen.queryByRole("list", { name: "Fired rules" })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: rows without pick data", () => {
    it("renders an unstamped row without chips, badge, or layout change", async () => {
      installBoothLogFetchMock({
        head: ok({ entries: [makeBoothLogEntry()], nextBefore: null }),
        personas: ok([]),
      });

      renderBoothLog();
      await flush();

      const summaryCell = screen.getByText("Started 'Astral Plane' by Valerie June").closest("td");
      expect(summaryCell).not.toBeNull();
      // No badge, no fired-rule list, and the cell's only rendered content is the summary text
      // itself — PickChips returned null rather than an empty wrapper.
      expect(screen.queryByText("Exploration pick")).not.toBeInTheDocument();
      expect(screen.queryByRole("list", { name: "Fired rules" })).not.toBeInTheDocument();
      expect(summaryCell?.textContent).toBe("Started 'Astral Plane' by Valerie June");
    });

    it("keeps the existing taste thumbs unaffected beside the new chips", async () => {
      installBoothLogFetchMock({
        head: ok({ entries: [makeBoothLogEntry({ personaId: 7 })], nextBefore: null }),
        personas: ok([{ id: 7, name: "Nova" }]),
      });

      renderBoothLog();
      await flush();

      // The row has no `pick` (unstamped) but IS persona-stamped for taste — the two features are
      // orthogonal, so the taste-thumb control renders exactly as it did before PickChips existed.
      expect(screen.getByRole("button", { name: "Taste up for Nova" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Taste down for Nova" })).toBeInTheDocument();
      expect(screen.queryByRole("list", { name: "Fired rules" })).not.toBeInTheDocument();
    });
  });
});
