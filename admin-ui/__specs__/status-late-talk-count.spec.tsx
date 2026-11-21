// @jest-environment jsdom
// PLAN T768 — "Talk skipped for lateness" on the Status page (SPEC F219.26, STORY-560 AC3)
// Runner: Jest (jsdom) + @testing-library/react. StatusTiles is prop-driven.

import { describe, it, expect, afterEach } from "@jest/globals";
import { render, screen, cleanup, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { StatusTiles } from "../app/(authed)/dashboard/StatusTiles";
import type { StatusResponse } from "@/lib/broadcast-api";

function makeStatus(talkDroppedLate7d: StatusResponse["talkDroppedLate7d"]): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 5 },
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
    voice: { engine: "kokoro", degraded: false, reason: null, checkedAt: null },
    talkDroppedLate7d,
  };
}

function lateTalkTile(): HTMLElement {
  return screen.getByRole("group", { name: "Talk skipped for lateness · last 7 days" });
}

afterEach(cleanup);

describe("Feature: the late-talk count", () => {
  describe("Scenario: 2 this week", () => {
    it("shows 2 for the last 7 days", () => {
      render(<StatusTiles status={makeStatus(2)} error={false} />);

      expect(within(lateTalkTile()).getByText("2")).toBeInTheDocument();
    });
  });
  describe("Scenario: none this week", () => {
    it("shows None", () => {
      render(<StatusTiles status={makeStatus(0)} error={false} />);

      expect(within(lateTalkTile()).getByText("None")).toBeInTheDocument();
    });
  });
  describe("Scenario: the count is unknown", () => {
    it("reads as unavailable", () => {
      render(<StatusTiles status={makeStatus(null)} error={false} />);

      expect(within(lateTalkTile()).getByText("Unavailable")).toBeInTheDocument();
    });
  });
});
