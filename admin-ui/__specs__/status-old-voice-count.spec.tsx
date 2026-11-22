// @jest-environment jsdom
// PLAN T765 — "N items still in the old voice" on the Status page (SPEC F219.23, STORY-559 AC5)
// Runner: Jest (jsdom) + @testing-library/react. StatusTiles is prop-driven.

import { describe, it, expect, afterEach } from "@jest/globals";
import { render, screen, cleanup, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { StatusTiles } from "../app/(authed)/dashboard/StatusTiles";
import type { StatusResponse } from "@/lib/broadcast-api";

function makeStatus(staleVoiceItems: StatusResponse["staleVoiceItems"]): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 5 },
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
    voice: { engine: "kokoro", degraded: false, reason: null, checkedAt: null },
    staleVoiceItems,
  };
}

function voiceTile(): HTMLElement {
  return screen.getByRole("group", { name: "Voice" });
}

afterEach(cleanup);

describe("Feature: the old-voice count", () => {
  describe("Scenario: 4 stale items", () => {
    it("shows 4 items still in the old voice", () => {
      render(<StatusTiles status={makeStatus(4)} error={false} />);

      expect(within(voiceTile()).getByText("4 items still in the old voice")).toBeInTheDocument();
    });
  });
  describe("Scenario: 1 stale item", () => {
    it("says item in the singular", () => {
      render(<StatusTiles status={makeStatus(1)} error={false} />);

      expect(within(voiceTile()).getByText("1 item still in the old voice")).toBeInTheDocument();
    });
  });
  describe("Scenario: none stale", () => {
    it("hides the line", () => {
      render(<StatusTiles status={makeStatus(0)} error={false} />);

      expect(within(voiceTile()).queryByText(/still in the old voice/)).not.toBeInTheDocument();
    });
  });
  describe("Scenario: the count is unknown", () => {
    it("reads as unavailable", () => {
      render(<StatusTiles status={makeStatus(null)} error={false} />);

      expect(within(voiceTile()).getByText("Old voice items: Unavailable")).toBeInTheDocument();
    });
  });
});
