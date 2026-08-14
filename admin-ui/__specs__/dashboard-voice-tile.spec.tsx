// @jest-environment jsdom
// PLAN T149 — Degraded voice on the health surface (SPEC F99.5, F100.3, STORY-256 AC4)
//
// Runner: Jest (jsdom) + @testing-library/react. StatusTiles is a pure, prop-driven presentational
// component (safe-scope-tile.spec.tsx's own idiom) — these specs render it directly with a built
// `status` prop rather than standing up DashboardView's three-endpoint fetch mock, since the
// Voice tile's own contract is what T149 added. The api half is
// GenWave.Tts.Tests/Specs/Story256_NeverSomeoneElsesVoice.cs's ScenarioTheHealthSurfaceShowsIt.

import { describe, it, expect, afterEach, jest } from "@jest/globals";
import { render, screen, cleanup } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { StatusTiles } from "../app/(authed)/dashboard/StatusTiles";
import type { StatusResponse } from "@/lib/broadcast-api";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

interface VoiceOverrides {
  engine?: string;
  degraded?: boolean;
  reason?: string | null;
  checkedAt?: string | null;
}

/** Catalog/SafeScope/LLM are fixed benign values — these specs exercise the Voice tile only. */
function makeStatus(voice: VoiceOverrides = {}): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 5 },
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
    voice: {
      engine: "kokoro",
      degraded: false,
      reason: null,
      checkedAt: null,
      ...voice,
    },
  };
}

function voiceTile(): HTMLElement {
  return screen.getByRole("group", { name: "Voice" });
}

afterEach(() => {
  cleanup();
  jest.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// Feature: Degraded voice on the health surface
// ---------------------------------------------------------------------------

describe("Feature: Degraded voice on the health surface", () => {
  describe("Scenario: tile states from /api/status voice", () => {
    it("renders ok with the engine name when the cached verdict is healthy", () => {
      render(<StatusTiles status={makeStatus({ engine: "kokoro", degraded: false })} error={false} />);

      expect(voiceTile().className).toMatch(/\bborder-success\b/);
      expect(screen.getByText("Kokoro")).toBeInTheDocument();
      expect(screen.getByText("Reachable")).toBeInTheDocument();
    });

    it("renders a warning naming the engine and the cause when the cached verdict is unhealthy", () => {
      render(
        <StatusTiles
          status={makeStatus({ engine: "kokoro", degraded: true, reason: "connection refused" })}
          error={false}
        />
      );

      expect(voiceTile().className).toMatch(/\bborder-danger\b/);
      expect(screen.getByText("Kokoro")).toBeInTheDocument();
      expect(screen.getByText("Engine down — DJ breaks are dropped, music keeps playing")).toBeInTheDocument();
      expect(screen.getByText("connection refused")).toBeInTheDocument();
    });

    it("distinguishes an engine-down state from an LLM copy-availability warning on the same poll", () => {
      // F99.5: an operator must be able to tell "the engine is down" from "the DJ has nothing to
      // say" — the two tiles must be independently able to warn without collapsing into one signal.
      render(
        <StatusTiles
          status={makeStatus({ engine: "piper", degraded: true, reason: "timed out" })}
          error={false}
        />
      );

      const llmTile = screen.getByRole("group", { name: "LLM" });
      expect(llmTile.className).toMatch(/\bborder-line\b/);
      expect(voiceTile().className).toMatch(/\bborder-danger\b/);
      expect(screen.getByText("Piper")).toBeInTheDocument();
    });

    it("shows a skeleton before the first poll resolves", () => {
      render(<StatusTiles status={null} error={false} />);

      expect(voiceTile()).toBeInTheDocument();
      expect(screen.queryByText("Reachable")).not.toBeInTheDocument();
    });
  });
});
