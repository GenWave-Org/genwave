// @jest-environment jsdom
// PLAN T766 — the Voice tile while the engine warms up (SPEC F219.25, STORY-560)
// Runner: Jest (jsdom) + @testing-library/react. StatusTiles is prop-driven.

import { describe, it, expect, afterEach } from "@jest/globals";
import { render, screen, cleanup, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { StatusTiles } from "../app/(authed)/dashboard/StatusTiles";
import type { StatusResponse } from "@/lib/broadcast-api";

function makeStatus(voice: StatusResponse["voice"]): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 5 },
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
    voice,
    staleVoiceItems: 0,
  };
}

const WARMING: StatusResponse["voice"] = {
  engine: "chatterbox",
  degraded: true,
  warming: true,
  reason: "Warming up: the engine has not rendered its first line yet.",
  checkedAt: null,
};

function voiceTile(): HTMLElement {
  return screen.getByRole("group", { name: "Voice" });
}

afterEach(cleanup);

describe("Feature: the voice tile while the engine warms up", () => {
  describe("Scenario: the warm-up line has not rendered yet", () => {
    it("says Warming up", () => {
      render(<StatusTiles status={makeStatus(WARMING)} error={false} />);

      expect(within(voiceTile()).getByText("Warming up")).toBeInTheDocument();
    });

    it("has an orange border", () => {
      render(<StatusTiles status={makeStatus(WARMING)} error={false} />);

      expect(voiceTile().className).toMatch(/\bborder-caution\b/);
    });

    it("does not say the engine is down", () => {
      render(<StatusTiles status={makeStatus(WARMING)} error={false} />);

      expect(within(voiceTile()).queryByText(/Engine down/)).not.toBeInTheDocument();
    });

    it("does not read as reachable", () => {
      render(<StatusTiles status={makeStatus(WARMING)} error={false} />);

      expect(within(voiceTile()).queryByText("Reachable")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: the engine is down", () => {
    it("says the engine is down and gives the reason", () => {
      const down = { engine: "chatterbox", degraded: true, warming: false, reason: "Connection refused", checkedAt: null };
      render(<StatusTiles status={makeStatus(down)} error={false} />);

      expect(within(voiceTile()).getByText("Connection refused")).toBeInTheDocument();
    });
  });
});
