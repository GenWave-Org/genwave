// @jest-environment jsdom
// STORY-353 — A red LLM tile names its cause (SPEC F139.2, the admin-ui half · PLAN T334)
//
// BDD specification — Jest (jsdom). The backend half rides
// tests/GenWave.Host.Tests/Specs/Story353_LlmCauseTaxonomy.cs (the /api/llm-calls surface) and
// Story125_LlmStatus.cs (the /api/status llm.dominantCause* fields this file's own tile reads).
//
// StatusTiles is a pure, prop-driven presentational component (`{ status, error, timeZone }` — no
// fetch of its own; DashboardView owns polling via usePoll, Q5/STORY-087) — these specs render it
// directly with a built `status` prop, mirroring safe-scope-tile.spec.tsx's own idiom, rather than
// standing up DashboardView's three-endpoint fetch mock: the tile rides the EXISTING /api/status
// poll (no new poller, the gh-#558 lesson), so there is nothing fetch-shaped left to prove here —
// dashboard-llm-tile.spec.tsx already covers that this data arrives via that one poll.

import { describe, it, expect, jest, afterEach } from "@jest/globals";
import { render, screen, cleanup } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { StatusTiles } from "../app/(authed)/dashboard/StatusTiles";
import type { StatusResponse } from "@/lib/broadcast-api";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

interface LlmOverrides {
  enabled?: boolean;
  model?: string | null;
  lastOutcome?: "ok" | "failed" | null;
  dominantCause?: string | null;
  dominantCauseCount?: number | null;
  dominantCauseModel?: string | null;
}

/** Catalog/SafeScope/Voice are fixed benign values — these specs exercise the LLM tile's own
 * dominant-cause line only. */
function makeStatus(llm: LlmOverrides = {}): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 5 },
    llm: {
      enabled: true,
      model: "gemma3:12b",
      activePersona: null,
      lastOutcome: "ok",
      lastAttemptAt: "2026-01-01T07:59:00.000Z",
      dominantCause: null,
      dominantCauseCount: null,
      dominantCauseModel: null,
      ...llm,
    },
    voice: { engine: "kokoro", degraded: false, reason: null, checkedAt: null },
  };
}

afterEach(() => {
  cleanup();
  jest.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// Feature: a red LLM tile names its cause
// ---------------------------------------------------------------------------

describe("Feature: a red LLM tile names its cause", () => {
  describe("Scenario: the tile explains a red verdict", () => {
    it("names the dominant recent cause when the LLM verdict is red (T334)", () => {
      render(
        <StatusTiles
          status={makeStatus({
            lastOutcome: "failed",
            dominantCause: "timeout",
            dominantCauseCount: 6,
            dominantCauseModel: "gemma3:12b",
          })}
          error={false}
        />
      );

      // SPEC F139.2's own worked example shape ("red: 6 timeouts…"), sentence-cased per house
      // copy rule — pluralized since the count is 6, not 1.
      expect(screen.getByText(/Red: 6 timeouts/)).toBeInTheDocument();
    });

    it("names the model alongside the cause (T334)", () => {
      render(
        <StatusTiles
          status={makeStatus({
            lastOutcome: "failed",
            dominantCause: "timeout",
            dominantCauseCount: 6,
            dominantCauseModel: "gemma3:12b",
          })}
          error={false}
        />
      );

      const line = screen.getByText(/Red: 6 timeouts/);
      expect(line).toHaveTextContent("gemma3:12b");
    });

    it("singularizes the noun for a count of exactly one", () => {
      render(
        <StatusTiles
          status={makeStatus({
            lastOutcome: "failed",
            dominantCause: "connectionfailure",
            dominantCauseCount: 1,
            dominantCauseModel: "llama3.1:8b",
          })}
          error={false}
        />
      );

      expect(screen.getByText("Red: 1 connection failure in the last 24h, llama3.1:8b")).toBeInTheDocument();
    });

    // T334 review round 1, advisory c — the same "never drop an unknown kind" discipline
    // LlmCallsFeed.tsx's CAUSE_LABELS already follows: a cause value shipped by the api ahead of an
    // admin-ui label update still renders, unstyled, as its raw wire token — never `undefined`,
    // never a thrown render.
    it("renders the raw wire token for a cause this tile has no specific label for", () => {
      render(
        <StatusTiles
          status={makeStatus({
            lastOutcome: "failed",
            dominantCause: "somenewcause",
            dominantCauseCount: 2,
            dominantCauseModel: "gemma3:12b",
          })}
          error={false}
        />
      );

      expect(screen.getByText("Red: 2 somenewcause in the last 24h, gemma3:12b")).toBeInTheDocument();
    });
  });

  describe("Scenario: quiet states stay quiet", () => {
    it("shows no cause line when the LLM verdict is green (T334)", () => {
      render(
        <StatusTiles
          status={makeStatus({
            lastOutcome: "ok",
            // Deliberately non-null: even if the api ever reported a dominant cause alongside an
            // "ok" last attempt, the tile only ever renders the line once the verdict is ALREADY
            // red (StatusTiles' own DominantCauseLine is gated on lastOutcome === "failed", not on
            // these fields alone) — a green tile must never show a "why" line for a fault that
            // didn't cause it to go red.
            dominantCause: "timeout",
            dominantCauseCount: 3,
            dominantCauseModel: "gemma3:12b",
          })}
          error={false}
        />
      );

      expect(screen.queryByText(/Red:/)).not.toBeInTheDocument();
    });

    it("shows no cause line when the LLM is disabled", () => {
      render(<StatusTiles status={makeStatus({ enabled: false, lastOutcome: null })} error={false} />);

      expect(screen.queryByText(/Red:/)).not.toBeInTheDocument();
    });

    it("shows no cause line for a failed verdict with nothing yet to explain it", () => {
      // The three dominantCause* fields travel together (broadcast-api.ts's own remarks) — a
      // failed verdict with none of them set renders the existing failure line alone, never an
      // empty or malformed "why" line.
      render(<StatusTiles status={makeStatus({ lastOutcome: "failed" })} error={false} />);

      expect(screen.getByText(/Last completion failed/)).toBeInTheDocument();
      expect(screen.queryByText(/Red:/)).not.toBeInTheDocument();
    });
  });
});
