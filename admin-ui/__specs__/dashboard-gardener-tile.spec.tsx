// @jest-environment jsdom
// SPEC F153.9, STORY-374 AC8, PLAN T378 — the dashboard's Gardener tile.
//
// Runner: Jest (jsdom) + @testing-library/react. StatusTiles is a pure, prop-driven presentational
// component (dashboard-voice-tile.spec.tsx/safe-scope-tile.spec.tsx's own idiom) — renders it
// directly with a built `status` prop rather than standing up DashboardView's three-endpoint fetch
// mock.
//
// T378 review LOW-1: the tile `Link` carries no `aria-label` override — it announces its OWN
// rendered content, so these specs query it by role alone (the Gardener tile is the only `Link`
// StatusTiles renders; every other tile stays a `role="group"` div) rather than by an exact
// accessible name.
//
// T378 review SMOKE-2: the breakdown line pluralises per kind ("1 dead file" vs "3 dead files",
// "1 near duplicate" vs "2 near duplicates"; "with stale metadata"/"unreachable"/"on the shelf" are
// phrased so they never need a singular/plural branch).

import { describe, it, expect, afterEach, jest } from "@jest/globals";
import { render, screen, cleanup } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { StatusTiles } from "../app/(authed)/dashboard/StatusTiles";
import type { StatusResponse } from "@/lib/broadcast-api";

function makeStatus(gardener: StatusResponse["gardener"]): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 5 },
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
    voice: { engine: "kokoro", degraded: false, reason: null, checkedAt: null },
    gardener,
  };
}

function gardenerTile(): HTMLElement {
  return screen.getByRole("link");
}

afterEach(() => {
  cleanup();
  jest.restoreAllMocks();
});

describe("Feature: the Gardener tile on the dashboard (SPEC F153.9, STORY-374 AC8)", () => {
  describe("Scenario: open counts from /api/status gardener", () => {
    it("renders the grand total", () => {
      render(
        <StatusTiles
          status={makeStatus({
            open: { deadFile: 5, nearDuplicate: 2, staleMetadata: 3, unreachable: 0, shelfDust: 1 },
            total: 11,
          })}
          error={false}
        />
      );

      expect(gardenerTile()).toHaveTextContent("11");
    });

    it("links to /gardener", () => {
      render(
        <StatusTiles
          status={makeStatus({
            open: { deadFile: 0, nearDuplicate: 0, staleMetadata: 0, unreachable: 0, shelfDust: 0 },
            total: 0,
          })}
          error={false}
        />
      );

      expect(gardenerTile()).toHaveAttribute("href", "/gardener");
    });
  });

  describe("Scenario: singular phrasing at a count of one (SMOKE-2)", () => {
    it("reads '1 open finding' (singular) for a total of one", () => {
      render(
        <StatusTiles
          status={makeStatus({
            open: { deadFile: 1, nearDuplicate: 0, staleMetadata: 0, unreachable: 0, shelfDust: 0 },
            total: 1,
          })}
          error={false}
        />
      );

      expect(gardenerTile()).toHaveTextContent("1 open finding");
    });

    it("pluralises the breakdown line's dead-file count for more than one", () => {
      render(
        <StatusTiles
          status={makeStatus({
            open: { deadFile: 3, nearDuplicate: 0, staleMetadata: 0, unreachable: 0, shelfDust: 0 },
            total: 3,
          })}
          error={false}
        />
      );

      expect(gardenerTile()).toHaveTextContent("3 dead files");
    });

    it("keeps the dead-file count singular at exactly one", () => {
      render(
        <StatusTiles
          status={makeStatus({
            open: { deadFile: 1, nearDuplicate: 0, staleMetadata: 0, unreachable: 0, shelfDust: 0 },
            total: 1,
          })}
          error={false}
        />
      );

      expect(gardenerTile()).toHaveTextContent("1 dead file ·");
    });
  });
});
